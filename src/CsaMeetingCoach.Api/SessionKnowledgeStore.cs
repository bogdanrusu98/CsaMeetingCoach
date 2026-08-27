using System.Collections.Concurrent;
using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using CsaMeetingCoach.Contracts;
using CsaMeetingCoach.Core;

namespace CsaMeetingCoach.Api;

public interface ISessionKnowledgeStore : ISessionKnowledgeReader, ISessionArtifactCleaner
{
    Task<StoredKnowledgeContent> StoreFileAsync(
        Guid sessionId,
        Guid sourceId,
        string displayName,
        KnowledgeSourceVisibility visibility,
        string fileName,
        string? mediaType,
        Stream source,
        CancellationToken cancellationToken);

    Task StoreLinkAsync(
        Guid sessionId,
        Guid sourceId,
        string displayName,
        KnowledgeSourceVisibility visibility,
        string content,
        CancellationToken cancellationToken);

    Task DeleteSourceAsync(
        Guid sessionId,
        Guid sourceId,
        CancellationToken cancellationToken);
}

public sealed record StoredKnowledgeContent(
    long SizeBytes,
    string? MediaType);

public sealed class LocalSessionKnowledgeStore(
    string rootDirectory,
    IKnowledgeMalwareScanner malwareScanner,
    IPdfKnowledgeExtractor? pdfExtractor = null) : ISessionKnowledgeStore
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _sessionLocks = new();
    private readonly IPdfKnowledgeExtractor _pdfExtractor =
        pdfExtractor ?? new InProcessPdfKnowledgeExtractor();

    public async Task<StoredKnowledgeContent> StoreFileAsync(
        Guid sessionId,
        Guid sourceId,
        string displayName,
        KnowledgeSourceVisibility visibility,
        string fileName,
        string? mediaType,
        Stream source,
        CancellationToken cancellationToken)
    {
        var extension = KnowledgeTextExtractor.ValidateExtension(fileName);
        var sessionDirectory = GetSessionDirectory(sessionId);
        var finalDirectory = GetSourceDirectory(sessionId, sourceId);
        var quarantineDirectory = GetQuarantineSessionDirectory(sessionId);
        var temporaryDirectory = Path.Combine(
            quarantineDirectory,
            string.Concat(sourceId.ToString("N"), ".", Guid.NewGuid().ToString("N"), ".tmp"));
        var originalPath = Path.Combine(temporaryDirectory, string.Concat("original", extension));
        var gate = _sessionLocks.GetOrAdd(sessionId, static _ => new SemaphoreSlim(1, 1));

        try
        {
            Directory.CreateDirectory(quarantineDirectory);
            Directory.CreateDirectory(temporaryDirectory);
            var sizeBytes = await CopyWithLimitAsync(
                source,
                originalPath,
                SessionKnowledgeLimits.MaximumFileBytes,
                cancellationToken);
            if (sizeBytes == 0)
            {
                throw new ArgumentException("Knowledge files cannot be empty.", nameof(source));
            }

            var scan = await malwareScanner.ScanAsync(originalPath, cancellationToken);
            if (scan != KnowledgeMalwareScanResult.Clean)
            {
                throw scan == KnowledgeMalwareScanResult.ThreatDetected
                    ? new KnowledgeRejectedException(
                        "The knowledge file was rejected by malware scanning.")
                    : new KnowledgeScannerUnavailableException(
                        "Knowledge upload is unavailable because malware scanning could not complete.");
            }

            var extractedText = await KnowledgeTextExtractor.ExtractFileAsync(
                originalPath,
                extension,
                _pdfExtractor,
                cancellationToken);
            await WriteContentAndMetadataAsync(
                temporaryDirectory,
                new KnowledgeContentMetadata(
                    sourceId,
                    displayName,
                    visibility,
                    DateTimeOffset.UtcNow),
                extractedText,
                cancellationToken);

            await gate.WaitAsync(cancellationToken);
            try
            {
                Directory.CreateDirectory(sessionDirectory);
                if (Directory.Exists(finalDirectory))
                {
                    throw new InvalidOperationException(
                        $"Knowledge source {sourceId} already exists.");
                }

                Directory.Move(temporaryDirectory, finalDirectory);
            }
            finally
            {
                gate.Release();
            }

            return new StoredKnowledgeContent(sizeBytes, NormalizeMediaType(mediaType));
        }
        finally
        {
            if (Directory.Exists(temporaryDirectory))
            {
                Directory.Delete(temporaryDirectory, recursive: true);
            }

            TryDeleteEmptyDirectory(quarantineDirectory);
        }
    }

    public async Task StoreLinkAsync(
        Guid sessionId,
        Guid sourceId,
        string displayName,
        KnowledgeSourceVisibility visibility,
        string content,
        CancellationToken cancellationToken)
    {
        var finalDirectory = GetSourceDirectory(sessionId, sourceId);
        var quarantineDirectory = GetQuarantineSessionDirectory(sessionId);
        var temporaryDirectory = Path.Combine(
            quarantineDirectory,
            string.Concat(sourceId.ToString("N"), ".", Guid.NewGuid().ToString("N"), ".tmp"));
        var gate = _sessionLocks.GetOrAdd(sessionId, static _ => new SemaphoreSlim(1, 1));

        try
        {
            Directory.CreateDirectory(quarantineDirectory);
            Directory.CreateDirectory(temporaryDirectory);
            await WriteContentAndMetadataAsync(
                temporaryDirectory,
                new KnowledgeContentMetadata(
                    sourceId,
                    displayName,
                    visibility,
                    DateTimeOffset.UtcNow),
                KnowledgeTextExtractor.NormalizeAndLimit(content),
                cancellationToken);

            await gate.WaitAsync(cancellationToken);
            try
            {
                Directory.CreateDirectory(GetSessionDirectory(sessionId));
                if (Directory.Exists(finalDirectory))
                {
                    throw new InvalidOperationException(
                        $"Knowledge source {sourceId} already exists.");
                }

                Directory.Move(temporaryDirectory, finalDirectory);
            }
            finally
            {
                gate.Release();
            }
        }
        finally
        {
            if (Directory.Exists(temporaryDirectory))
            {
                Directory.Delete(temporaryDirectory, recursive: true);
            }

            TryDeleteEmptyDirectory(quarantineDirectory);
        }
    }

    public async Task<IReadOnlyList<SessionKnowledgeSnippet>> ReadAsync(
        Guid sessionId,
        IReadOnlyCollection<Guid> allowedSourceIds,
        CancellationToken cancellationToken)
    {
        var sessionDirectory = GetSessionDirectory(sessionId);
        if (!Directory.Exists(sessionDirectory) || allowedSourceIds.Count == 0)
        {
            return [];
        }

        var gate = _sessionLocks.GetOrAdd(sessionId, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var allowed = allowedSourceIds.ToHashSet();
            var snippets = new List<(DateTimeOffset CreatedAtUtc, SessionKnowledgeSnippet Snippet)>();
            var totalCharacters = 0;
            foreach (var sourceId in allowed)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var directory = GetSourceDirectory(sessionId, sourceId);
                var metadataPath = Path.Combine(directory, "metadata.json");
                var contentPath = Path.Combine(directory, "content.txt");
                if (!File.Exists(metadataPath) || !File.Exists(contentPath))
                {
                    continue;
                }

                await using var metadataStream = File.OpenRead(metadataPath);
                var metadata = await JsonSerializer.DeserializeAsync<KnowledgeContentMetadata>(
                    metadataStream,
                    JsonOptions,
                    cancellationToken);
                if (metadata is null
                    || metadata.SourceId != sourceId
                    || !allowed.Contains(metadata.SourceId))
                {
                    continue;
                }

                var content = await File.ReadAllTextAsync(contentPath, cancellationToken);
                var remaining = SessionKnowledgeLimits.MaximumPromptCharacters - totalCharacters;
                if (remaining <= 0)
                {
                    break;
                }

                if (content.Length > remaining)
                {
                    content = content[..remaining];
                }

                totalCharacters += content.Length;
                snippets.Add((
                    metadata.CreatedAtUtc,
                    new SessionKnowledgeSnippet(
                        metadata.SourceId,
                        metadata.DisplayName,
                        metadata.Visibility,
                        content)));
            }

            return snippets
                .OrderBy(item => item.CreatedAtUtc)
                .Select(item => item.Snippet)
                .ToArray();
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task DeleteSourceAsync(
        Guid sessionId,
        Guid sourceId,
        CancellationToken cancellationToken)
    {
        var gate = _sessionLocks.GetOrAdd(sessionId, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var path = GetSourceDirectory(sessionId, sourceId);
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
            DeleteMatchingQuarantineSources(sessionId, sourceId);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task DeleteSessionArtifactsAsync(
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        var gate = _sessionLocks.GetOrAdd(sessionId, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var path = GetSessionDirectory(sessionId);
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
            var quarantinePath = GetQuarantineSessionDirectory(sessionId);
            if (Directory.Exists(quarantinePath))
            {
                Directory.Delete(quarantinePath, recursive: true);
            }
        }
        finally
        {
            gate.Release();
        }
    }

    private static async Task<long> CopyWithLimitAsync(
        Stream source,
        string destinationPath,
        long maximumBytes,
        CancellationToken cancellationToken)
    {
        await using var destination = new FileStream(
            destinationPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 81_920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var buffer = new byte[81_920];
        long total = 0;
        while (true)
        {
            var read = await source.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                return total;
            }

            total = checked(total + read);
            if (total > maximumBytes)
            {
                throw new BadHttpRequestException(
                    $"Knowledge files cannot exceed {SessionKnowledgeLimits.MaximumFileMegabytes} MB.");
            }

            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
    }

    private static async Task WriteContentAndMetadataAsync(
        string directory,
        KnowledgeContentMetadata metadata,
        string content,
        CancellationToken cancellationToken)
    {
        await File.WriteAllTextAsync(
            Path.Combine(directory, "content.txt"),
            content,
            Encoding.UTF8,
            cancellationToken);
        await using var metadataStream = File.Create(Path.Combine(directory, "metadata.json"));
        await JsonSerializer.SerializeAsync(
            metadataStream,
            metadata,
            JsonOptions,
            cancellationToken);
    }

    private string GetSessionDirectory(Guid sessionId) =>
        Path.Combine(rootDirectory, sessionId.ToString("N"));

    private string GetSourceDirectory(Guid sessionId, Guid sourceId) =>
        Path.Combine(GetSessionDirectory(sessionId), sourceId.ToString("N"));

    private string GetQuarantineSessionDirectory(Guid sessionId) =>
        Path.Combine(rootDirectory, ".quarantine", sessionId.ToString("N"));

    private void DeleteMatchingQuarantineSources(Guid sessionId, Guid sourceId)
    {
        var directory = GetQuarantineSessionDirectory(sessionId);
        if (!Directory.Exists(directory))
        {
            return;
        }

        foreach (var path in Directory.EnumerateDirectories(
            directory,
            string.Concat(sourceId.ToString("N"), ".*.tmp")))
        {
            Directory.Delete(path, recursive: true);
        }
        TryDeleteEmptyDirectory(directory);
    }

    private static void TryDeleteEmptyDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path)
                && !Directory.EnumerateFileSystemEntries(path).Any())
            {
                Directory.Delete(path);
            }
        }
        catch (IOException)
        {
            // A concurrent upload may still be using the quarantine directory.
        }
        catch (UnauthorizedAccessException)
        {
            // Cleanup will retry the session quarantine directory later.
        }
    }

    private static string? NormalizeMediaType(string? mediaType)
    {
        if (string.IsNullOrWhiteSpace(mediaType))
        {
            return null;
        }

        var normalized = mediaType.Trim();
        return normalized.Length <= 128 ? normalized : normalized[..128];
    }

    private sealed record KnowledgeContentMetadata(
        Guid SourceId,
        string DisplayName,
        KnowledgeSourceVisibility Visibility,
        DateTimeOffset CreatedAtUtc);
}

internal static partial class KnowledgeTextExtractor
{
    private static readonly HashSet<string> SupportedExtensions =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ".docx",
            ".html",
            ".json",
            ".md",
            ".pdf",
            ".pptx",
            ".txt",
            ".xlsx"
        };

    public static string ValidateExtension(string fileName)
    {
        var safeName = Path.GetFileName(fileName);
        if (string.IsNullOrWhiteSpace(safeName)
            || safeName.Any(char.IsControl)
            || safeName.Length > 255)
        {
            throw new ArgumentException("Knowledge file name is invalid.", nameof(fileName));
        }

        var extension = Path.GetExtension(safeName);
        if (!SupportedExtensions.Contains(extension))
        {
            throw new ArgumentException(
                "Supported knowledge files are DOCX, HTML, JSON, Markdown, PDF, PPTX, TXT, and XLSX.",
                nameof(fileName));
        }

        return extension.ToLowerInvariant();
    }

    public static async Task<string> ExtractFileAsync(
        string path,
        string extension,
        IPdfKnowledgeExtractor pdfExtractor,
        CancellationToken cancellationToken)
    {
        var text = extension switch
        {
            ".docx" => await ExtractOpenXmlAsync(
                path,
                entry => string.Equals(entry, "word/document.xml", StringComparison.OrdinalIgnoreCase),
                cancellationToken),
            ".pptx" => await ExtractOpenXmlAsync(
                path,
                entry => entry.StartsWith("ppt/slides/slide", StringComparison.OrdinalIgnoreCase)
                    && entry.EndsWith(".xml", StringComparison.OrdinalIgnoreCase),
                cancellationToken),
            ".xlsx" => await ExtractOpenXmlAsync(
                path,
                entry => string.Equals(entry, "xl/sharedStrings.xml", StringComparison.OrdinalIgnoreCase)
                    || entry.StartsWith("xl/worksheets/sheet", StringComparison.OrdinalIgnoreCase)
                    && entry.EndsWith(".xml", StringComparison.OrdinalIgnoreCase),
                cancellationToken),
            ".html" => ExtractHtml(await ReadTextAsync(path, cancellationToken)),
            ".pdf" => await pdfExtractor.ExtractAsync(path, cancellationToken),
            _ => await ReadTextAsync(path, cancellationToken)
        };
        return NormalizeAndLimit(text);
    }

    public static string ExtractFetchedContent(string content, string mediaType) =>
        NormalizeAndLimit(mediaType.Contains("html", StringComparison.OrdinalIgnoreCase)
            ? ExtractHtml(content)
            : content);

    public static string NormalizeAndLimit(string text)
    {
        var normalized = WhitespaceRegex().Replace(text, " ").Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new KnowledgeRejectedException(
                "The knowledge source did not contain readable text.");
        }

        return normalized.Length <= SessionKnowledgeLimits.MaximumExtractedCharactersPerSource
            ? normalized
            : normalized[..SessionKnowledgeLimits.MaximumExtractedCharactersPerSource];
    }

    private static async Task<string> ReadTextAsync(
        string path,
        CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        using var reader = new StreamReader(
            stream,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true),
            detectEncodingFromByteOrderMarks: true,
            bufferSize: 4096,
            leaveOpen: false);
        try
        {
            var buffer = new char[8_192];
            var builder = new StringBuilder();
            while (builder.Length < SessionKnowledgeLimits.MaximumExtractedCharactersPerSource)
            {
                var remaining =
                    SessionKnowledgeLimits.MaximumExtractedCharactersPerSource - builder.Length;
                var read = await reader.ReadAsync(
                    buffer.AsMemory(0, Math.Min(buffer.Length, remaining)),
                    cancellationToken);
                if (read == 0)
                {
                    break;
                }

                builder.Append(buffer, 0, read);
            }

            return builder.ToString();
        }
        catch (DecoderFallbackException exception)
        {
            throw new KnowledgeRejectedException(
                "The knowledge file text encoding is not supported.",
                exception);
        }
    }

    private static async Task<string> ExtractOpenXmlAsync(
        string path,
        Func<string, bool> includeEntry,
        CancellationToken cancellationToken)
    {
        const int maximumEntries = 1_000;
        const long maximumExpandedBytes = 20L * 1024 * 1024;
        using var archive = ZipFile.OpenRead(path);
        if (archive.Entries.Count > maximumEntries)
        {
            throw new KnowledgeRejectedException(
                "The Office document contains too many archive entries.");
        }

        var selectedEntries = archive.Entries
            .Where(entry => includeEntry(entry.FullName))
            .OrderBy(entry => entry.FullName, StringComparer.Ordinal)
            .ToArray();

        var builder = new StringBuilder();
        long actualExpandedBytes = 0;
        var settings = new XmlReaderSettings
        {
            Async = true,
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = maximumExpandedBytes
        };
        foreach (var entry in selectedEntries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await using var compressedStream = entry.Open();
            using var expandedStream = new MemoryStream();
            var buffer = new byte[32_768];
            while (true)
            {
                var read = await compressedStream.ReadAsync(buffer, cancellationToken);
                if (read == 0)
                {
                    break;
                }

                actualExpandedBytes = checked(actualExpandedBytes + read);
                if (actualExpandedBytes > maximumExpandedBytes)
                {
                    throw new KnowledgeRejectedException(
                        "The expanded Office document exceeds the safe extraction limit.");
                }

                await expandedStream.WriteAsync(
                    buffer.AsMemory(0, read),
                    cancellationToken);
            }

            expandedStream.Position = 0;
            using var reader = XmlReader.Create(expandedStream, settings);
            while (await reader.ReadAsync())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (reader.NodeType is XmlNodeType.Text or XmlNodeType.CDATA
                    && !string.IsNullOrWhiteSpace(reader.Value))
                {
                    builder.Append(reader.Value).Append(' ');
                    if (builder.Length >=
                        SessionKnowledgeLimits.MaximumExtractedCharactersPerSource)
                    {
                        return builder
                            .ToString(
                                0,
                                SessionKnowledgeLimits.MaximumExtractedCharactersPerSource);
                    }
                }
            }
        }

        return builder.ToString();
    }

    private static string ExtractHtml(string html)
    {
        try
        {
            var withoutExecutableContent = ScriptAndStyleRegex().Replace(html, " ");
            var withoutTags = HtmlTagRegex().Replace(withoutExecutableContent, " ");
            return WebUtility.HtmlDecode(withoutTags);
        }
        catch (RegexMatchTimeoutException exception)
        {
            throw new KnowledgeRejectedException(
                "The HTML knowledge source is too complex to process safely.",
                exception);
        }
    }

    [GeneratedRegex(
        @"<(script|style)\b[^>]*>.*?</\1\s*>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 1_000)]
    private static partial Regex ScriptAndStyleRegex();

    [GeneratedRegex(
        @"<[^>]+>",
        RegexOptions.Singleline | RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 1_000)]
    private static partial Regex HtmlTagRegex();

    [GeneratedRegex(
        @"\s+",
        RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 1_000)]
    private static partial Regex WhitespaceRegex();
}

public class KnowledgeRejectedException : InvalidOperationException
{
    public KnowledgeRejectedException(string message) : base(message)
    {
    }

    public KnowledgeRejectedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

public sealed class KnowledgeScannerUnavailableException(string message)
    : InvalidOperationException(message);
