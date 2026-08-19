using System.Net;
using System.Net.Sockets;
using System.Text;

namespace CsaMeetingCoach.Api;

public sealed record FetchedKnowledgeLink(
    Uri SafeSourceUri,
    string DisplayName,
    string MediaType,
    long SizeBytes,
    string Content);

public interface IKnowledgeLinkFetcher
{
    Task<FetchedKnowledgeLink> FetchAsync(
        Uri source,
        string? requestedDisplayName,
        CancellationToken cancellationToken);
}

public sealed class SafeKnowledgeLinkFetcher : IKnowledgeLinkFetcher
{
    private const int MaximumRedirects = 3;
    private const int MaximumBytes = 2 * 1024 * 1024;
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(15);
    private static readonly HashSet<string> AllowedMediaTypes =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "application/json",
            "application/xhtml+xml",
            "text/html",
            "text/markdown",
            "text/plain"
        };

    public async Task<FetchedKnowledgeLink> FetchAsync(
        Uri source,
        string? requestedDisplayName,
        CancellationToken cancellationToken)
    {
        var current = ValidateUri(source);
        for (var redirect = 0; redirect <= MaximumRedirects; redirect++)
        {
            var addresses = await ResolvePublicAddressesAsync(current, cancellationToken);
            using var handler = CreatePinnedHandler(addresses[0]);
            using var client = new HttpClient(handler)
            {
                Timeout = Timeout.InfiniteTimeSpan
            };
            using var request = new HttpRequestMessage(HttpMethod.Get, current);
            request.Headers.UserAgent.ParseAdd("CsaMeetingCoach-Knowledge/1.0");
            request.Headers.Accept.ParseAdd(
                "text/html, text/plain, text/markdown, application/json, application/xhtml+xml");
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(RequestTimeout);
            HttpResponseMessage response;
            try
            {
                response = await client.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    timeout.Token);
            }
            catch (OperationCanceledException exception)
                when (!cancellationToken.IsCancellationRequested)
            {
                throw new KnowledgeRejectedException(
                    "The knowledge link request timed out.",
                    exception);
            }
            catch (HttpRequestException exception)
            {
                throw new KnowledgeRejectedException(
                    "The knowledge link could not be reached securely.",
                    exception);
            }

            using (response)
            {

                if (IsRedirect(response.StatusCode))
                {
                    if (redirect == MaximumRedirects || response.Headers.Location is null)
                    {
                        throw new KnowledgeRejectedException(
                            "The knowledge link exceeded the redirect limit.");
                    }

                    current = ValidateUri(response.Headers.Location.IsAbsoluteUri
                        ? response.Headers.Location
                        : new Uri(current, response.Headers.Location));
                    continue;
                }

                if (!response.IsSuccessStatusCode)
                {
                    throw new KnowledgeRejectedException(
                        $"The knowledge link returned HTTP {(int)response.StatusCode}.");
                }

                var mediaType = response.Content.Headers.ContentType?.MediaType;
                if (mediaType is null || !AllowedMediaTypes.Contains(mediaType))
                {
                    throw new KnowledgeRejectedException(
                        "The knowledge link must return supported text or HTML content.");
                }

                if (response.Content.Headers.ContentLength is > MaximumBytes)
                {
                    throw new KnowledgeRejectedException(
                        "Knowledge link content cannot exceed 2 MB.");
                }

                var bytes = await ReadWithLimitAsync(
                    await response.Content.ReadAsStreamAsync(timeout.Token),
                    timeout.Token);
                var encoding = ResolveEncoding(response.Content.Headers.ContentType?.CharSet);
                var rawContent = encoding.GetString(bytes);
                var content = KnowledgeTextExtractor.ExtractFetchedContent(rawContent, mediaType);
                return new FetchedKnowledgeLink(
                    RemoveQueryAndFragment(current),
                    NormalizeDisplayName(requestedDisplayName, current),
                    mediaType,
                    bytes.LongLength,
                    content);
            }
        }

        throw new KnowledgeRejectedException("The knowledge link could not be fetched.");
    }

    internal static Uri ValidateUri(Uri source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!source.IsAbsoluteUri
            || source.Scheme != Uri.UriSchemeHttps
            || string.IsNullOrWhiteSpace(source.Host)
            || !string.IsNullOrEmpty(source.UserInfo)
            || source.Port != 443)
        {
            throw new ArgumentException(
                "Knowledge links must use public HTTPS on port 443 without embedded credentials.",
                nameof(source));
        }

        return source;
    }

    internal static bool IsPublicAddress(IPAddress address)
    {
        if (IPAddress.IsLoopback(address)
            || address.Equals(IPAddress.Any)
            || address.Equals(IPAddress.IPv6Any)
            || address.IsIPv6LinkLocal
            || address.IsIPv6SiteLocal
            || address.IsIPv6Multicast)
        {
            return false;
        }

        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        var bytes = address.GetAddressBytes();
        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            return bytes[0] switch
            {
                0 or 10 or 127 => false,
                100 when bytes[1] is >= 64 and <= 127 => false,
                169 when bytes[1] == 254 => false,
                172 when bytes[1] is >= 16 and <= 31 => false,
                192 when bytes[1] == 0 || bytes[1] == 168 => false,
                198 when bytes[1] is 18 or 19 => false,
                >= 224 => false,
                _ => true
            };
        }

        return address.AddressFamily == AddressFamily.InterNetworkV6
            && (bytes[0] & 0xFE) != 0xFC;
    }

    private static async Task<IPAddress[]> ResolvePublicAddressesAsync(
        Uri uri,
        CancellationToken cancellationToken)
    {
        IPAddress[] addresses;
        if (IPAddress.TryParse(uri.DnsSafeHost, out var literalAddress))
        {
            addresses = [literalAddress];
        }
        else
        {
            addresses = await Dns.GetHostAddressesAsync(
                uri.DnsSafeHost,
                cancellationToken);
        }

        if (addresses.Length == 0 || addresses.Any(address => !IsPublicAddress(address)))
        {
            throw new KnowledgeRejectedException(
                "The knowledge link resolves to a blocked or non-public address.");
        }

        return addresses;
    }

    private static SocketsHttpHandler CreatePinnedHandler(IPAddress address)
    {
        return new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            AutomaticDecompression =
                DecompressionMethods.Brotli | DecompressionMethods.Deflate | DecompressionMethods.GZip,
            ConnectCallback = async (context, cancellationToken) =>
            {
                var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                try
                {
                    await socket.ConnectAsync(
                        address,
                        context.DnsEndPoint.Port,
                        cancellationToken);
                    return new NetworkStream(socket, ownsSocket: true);
                }
                catch
                {
                    socket.Dispose();
                    throw;
                }
            }
        };
    }

    private static async Task<byte[]> ReadWithLimitAsync(
        Stream stream,
        CancellationToken cancellationToken)
    {
        using var output = new MemoryStream();
        var buffer = new byte[32_768];
        while (true)
        {
            var read = await stream.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                return output.ToArray();
            }

            if (output.Length + read > MaximumBytes)
            {
                throw new KnowledgeRejectedException(
                    "Knowledge link content cannot exceed 2 MB.");
            }

            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
    }

    private static Encoding ResolveEncoding(string? charset)
    {
        if (string.IsNullOrWhiteSpace(charset))
        {
            return Encoding.UTF8;
        }

        try
        {
            return Encoding.GetEncoding(charset.Trim('"', ' '));
        }
        catch (ArgumentException exception)
        {
            throw new KnowledgeRejectedException(
                "The knowledge link uses an unsupported text encoding.",
                exception);
        }
    }

    private static string NormalizeDisplayName(string? requestedName, Uri source)
    {
        var name = string.IsNullOrWhiteSpace(requestedName)
            ? source.Host
            : requestedName.Trim();
        if (name.Length > 120 || name.Any(char.IsControl))
        {
            throw new ArgumentException(
                "Knowledge link display name cannot exceed 120 characters.");
        }

        return name;
    }

    private static Uri RemoveQueryAndFragment(Uri source)
    {
        var builder = new UriBuilder(source)
        {
            Query = string.Empty,
            Fragment = string.Empty
        };
        return builder.Uri;
    }

    private static bool IsRedirect(HttpStatusCode statusCode) =>
        statusCode is HttpStatusCode.Moved
            or HttpStatusCode.Redirect
            or HttpStatusCode.RedirectMethod
            or HttpStatusCode.TemporaryRedirect
            or HttpStatusCode.PermanentRedirect;
}
