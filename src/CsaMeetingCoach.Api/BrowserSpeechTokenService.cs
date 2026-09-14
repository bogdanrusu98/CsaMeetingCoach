using System.Net;
using System.Text.RegularExpressions;
using CsaMeetingCoach.Core;

namespace CsaMeetingCoach.Api;

public sealed class BrowserSpeechOptions
{
    public bool Enabled { get; init; }
    public string SubscriptionKey { get; init; } = string.Empty;
    public string Region { get; init; } = string.Empty;
    public string Language { get; init; } = "en-US";
    public string EndpointId { get; init; } = string.Empty;

    public void Validate()
    {
        if (!Enabled)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(SubscriptionKey))
        {
            throw new InvalidOperationException(
                "BrowserSpeech:SubscriptionKey is required when browser speech is enabled.");
        }

        if (!Regex.IsMatch(Region, "^[a-z0-9-]+$", RegexOptions.CultureInvariant))
        {
            throw new InvalidOperationException(
                "BrowserSpeech:Region must be a valid Azure Speech region name.");
        }

        if (!Regex.IsMatch(
                Language,
                "^[A-Za-z]{2,3}(?:-[A-Za-z0-9]{2,8})+$",
                RegexOptions.CultureInvariant))
        {
            throw new InvalidOperationException(
                "BrowserSpeech:Language must be a valid speech locale such as en-US.");
        }

        if (!string.IsNullOrEmpty(EndpointId)
            && (!Guid.TryParseExact(EndpointId, "D", out var endpointId)
                || endpointId == Guid.Empty
                || !string.Equals(
                    endpointId.ToString("D"),
                    EndpointId,
                    StringComparison.Ordinal)))
        {
            throw new InvalidOperationException(
                "BrowserSpeech:EndpointId must be empty or a canonical GUID.");
        }
    }
}

public sealed record BrowserSpeechTokenResponse(
    string Token,
    string Region,
    string Language,
    DateTimeOffset ExpiresAtUtc)
{
    public IReadOnlyList<string> Phrases { get; init; } = [];
    public string? EndpointId { get; init; }
}

public interface IBrowserSpeechTokenService
{
    Task<BrowserSpeechTokenResponse> IssueTokenAsync(
        CancellationToken cancellationToken);
}

public sealed class BrowserSpeechUnavailableException : Exception
{
    public BrowserSpeechUnavailableException(string message)
        : base(message)
    {
    }
}

internal sealed class AzureBrowserSpeechTokenService(
    HttpClient httpClient,
    BrowserSpeechOptions options,
    TimeProvider timeProvider) : IBrowserSpeechTokenService
{
    public async Task<BrowserSpeechTokenResponse> IssueTokenAsync(
        CancellationToken cancellationToken)
    {
        if (!options.Enabled)
        {
            throw new BrowserSpeechUnavailableException(
                "Browser microphone transcription is not configured.");
        }

        var tokenEndpoint = new Uri(
            $"https://{options.Region}.api.cognitive.microsoft.com/sts/v1.0/issueToken");
        using var request = new HttpRequestMessage(HttpMethod.Post, tokenEndpoint)
        {
            Content = new ByteArrayContent([])
        };
        request.Headers.TryAddWithoutValidation(
            "Ocp-Apim-Subscription-Key",
            options.SubscriptionKey);

        using var response = await httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        if (response.StatusCode != HttpStatusCode.OK)
        {
            throw new BrowserSpeechUnavailableException(
                $"Azure Speech token request failed with status {(int)response.StatusCode}.");
        }

        var token = (await response.Content.ReadAsStringAsync(cancellationToken)).Trim();
        if (string.IsNullOrWhiteSpace(token) || token.Length > 16_384)
        {
            throw new BrowserSpeechUnavailableException(
                "Azure Speech returned an invalid authorization token.");
        }

        return new BrowserSpeechTokenResponse(
            token,
            options.Region,
            options.Language,
            timeProvider.GetUtcNow().AddMinutes(9))
        {
            Phrases = EducationalConceptCatalog.BuildSpeechPhraseVocabulary(),
            EndpointId = string.IsNullOrEmpty(options.EndpointId)
                ? null
                : options.EndpointId
        };
    }
}
