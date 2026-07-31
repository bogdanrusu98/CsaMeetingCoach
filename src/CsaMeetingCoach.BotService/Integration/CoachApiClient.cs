using System.Net.Http.Headers;
using System.Net.Http.Json;
using Azure.Core;
using CsaMeetingCoach.BotService.Configuration;
using CsaMeetingCoach.BotService.Media;
using CsaMeetingCoach.Contracts;
using Microsoft.Extensions.Options;

namespace CsaMeetingCoach.BotService.Integration;

public sealed class CoachApiClient : ICoachApiClient
{
    private readonly HttpClient httpClient;
    private readonly CoachApiOptions options;
    private readonly TokenCredential credential;

    public CoachApiClient(
        HttpClient httpClient,
        IOptions<CoachApiOptions> options,
        TokenCredential credential)
    {
        this.httpClient = httpClient;
        this.options = options.Value;
        this.credential = credential;
    }

    public async Task PublishAsync(
        Guid coachSessionId,
        string teamsOnlineMeetingId,
        Guid sourceSegmentId,
        FinalTranscript transcript,
        CancellationToken cancellationToken)
    {
        var payload = new AdapterTranscriptSegmentRequest(
            teamsOnlineMeetingId,
            new AddTranscriptSegmentRequest(
                "Meeting participant",
                transcript.Text,
                transcript.OccurredAtUtc,
                IsFinal: true,
                sourceSegmentId));

        for (var attempt = 1; attempt <= options.MaxAttempts; attempt++)
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                new Uri(options.BaseUrl!, $"api/adapter/sessions/{coachSessionId:D}/transcript"))
            {
                Content = JsonContent.Create(payload)
            };
            await AuthenticateAsync(request, cancellationToken).ConfigureAwait(false);

            try
            {
                using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
                if (response.IsSuccessStatusCode)
                {
                    return;
                }

                if (!IsTransient(response.StatusCode))
                {
                    throw new HttpRequestException(
                        $"Coach API rejected transcript with nontransient HTTP {(int)response.StatusCode}.",
                        null,
                        response.StatusCode);
                }

                if (attempt == options.MaxAttempts)
                {
                    throw new HttpRequestException(
                        $"Coach API transcript publish exhausted {options.MaxAttempts} attempts after HTTP {(int)response.StatusCode}.",
                        null,
                        response.StatusCode);
                }
            }
            catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
            {
                if (attempt == options.MaxAttempts)
                {
                    throw new HttpRequestException(
                        $"Coach API transcript publish exhausted {options.MaxAttempts} attempts after an HTTP timeout.",
                        exception);
                }
            }
            catch (HttpRequestException exception) when (
                !exception.StatusCode.HasValue || IsTransient(exception.StatusCode.Value))
            {
                if (attempt == options.MaxAttempts)
                {
                    throw new HttpRequestException(
                        $"Coach API transcript publish exhausted {options.MaxAttempts} attempts.",
                        exception,
                        exception.StatusCode);
                }
            }

            await Task.Delay(Backoff(attempt), cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task AuthenticateAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (options.AuthenticationMode == EndpointAuthenticationMode.DevelopmentApiKey)
        {
            request.Headers.Add("X-Transcript-Adapter-Key", options.DevelopmentApiKey);
            return;
        }

        var token = await credential.GetTokenAsync(
            new TokenRequestContext([options.EntraScope]),
            cancellationToken).ConfigureAwait(false);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
    }

    private static TimeSpan Backoff(int attempt) =>
        TimeSpan.FromMilliseconds(Math.Min(250 * Math.Pow(2, attempt - 1), 2_000));

    private static bool IsTransient(System.Net.HttpStatusCode statusCode)
    {
        var code = (int)statusCode;
        return code is 408 or 429 || code is >= 500 and <= 599;
    }
}
