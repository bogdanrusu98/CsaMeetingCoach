using System.Net;
using System.Text.Json;
using Azure.Core;
using CsaMeetingCoach.BotService.Configuration;
using CsaMeetingCoach.BotService.Integration;
using CsaMeetingCoach.BotService.Media;
using Microsoft.Extensions.Options;

namespace CsaMeetingCoach.BotService.Tests;

public sealed class CoachApiClientTests
{
    [Fact]
    public async Task RetriesTransientFailureWithSameSourceSegmentId()
    {
        var handler = new RecordingHandler();
        var options = Options.Create(new CoachApiOptions
        {
            BaseUrl = new Uri("https://coach.example/"),
            AuthenticationMode = EndpointAuthenticationMode.DevelopmentApiKey,
            DevelopmentApiKey = new string('k', 32),
            MaxAttempts = 2
        });
        var client = new CoachApiClient(
            new HttpClient(handler),
            options,
            new FakeCredential());
        var sourceId = Guid.NewGuid();

        await client.PublishAsync(
            Guid.NewGuid(),
            "meeting",
            sourceId,
            new FinalTranscript("result", "hello", DateTimeOffset.UtcNow),
            CancellationToken.None);

        Assert.Equal(2, handler.SourceIds.Count);
        Assert.All(handler.SourceIds, id => Assert.Equal(sourceId, id));
        Assert.All(handler.ApiKeys, key => Assert.Equal(new string('k', 32), key));
        Assert.All(handler.Speakers, speaker => Assert.Equal("Meeting participant", speaker));
        Assert.All(handler.SpeechRecognitionMarkers, Assert.True);
    }

    [Fact]
    public async Task RetriesHttpClientTimeoutWhenCallerDidNotCancel()
    {
        var handler = new TimeoutOnceHandler();
        var client = CreateClient(handler, maxAttempts: 2);

        await client.PublishAsync(
            Guid.NewGuid(),
            "meeting",
            Guid.NewGuid(),
            new FinalTranscript("result", "hello", DateTimeOffset.UtcNow),
            CancellationToken.None);

        Assert.Equal(2, handler.Attempts);
    }

    [Fact]
    public async Task RetriesArbitraryServerError()
    {
        var handler = new RecordingHandler((HttpStatusCode)599);
        var client = CreateClient(handler, maxAttempts: 2);

        await Assert.ThrowsAsync<HttpRequestException>(() => client.PublishAsync(
            Guid.NewGuid(),
            "meeting",
            Guid.NewGuid(),
            new FinalTranscript("result", "hello", DateTimeOffset.UtcNow),
            CancellationToken.None));

        Assert.Equal(2, handler.SourceIds.Count);
    }

    [Fact]
    public async Task DoesNotRetryNonTransientRejection()
    {
        var handler = new RecordingHandler(HttpStatusCode.BadRequest);
        var client = new CoachApiClient(
            new HttpClient(handler),
            Options.Create(new CoachApiOptions
            {
                BaseUrl = new Uri("https://coach.example/"),
                AuthenticationMode = EndpointAuthenticationMode.DevelopmentApiKey,
                DevelopmentApiKey = new string('k', 32),
                MaxAttempts = 4
            }),
            new FakeCredential());

        await Assert.ThrowsAsync<HttpRequestException>(() => client.PublishAsync(
            Guid.NewGuid(),
            "meeting",
            Guid.NewGuid(),
            new FinalTranscript("result", "hello", DateTimeOffset.UtcNow),
            CancellationToken.None));
        Assert.Single(handler.SourceIds);
    }

    private sealed class RecordingHandler(HttpStatusCode? fixedStatus = null) : HttpMessageHandler
    {
        public List<Guid> SourceIds { get; } = [];
        public List<string> ApiKeys { get; } = [];
        public List<string> Speakers { get; } = [];
        public List<bool> SpeechRecognitionMarkers { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var json = await request.Content!.ReadAsStringAsync(cancellationToken);
            using var document = JsonDocument.Parse(json);
            SourceIds.Add(document.RootElement
                .GetProperty("segment")
                .GetProperty("sourceSegmentId")
                .GetGuid());
            Speakers.Add(document.RootElement
                .GetProperty("segment")
                .GetProperty("speaker")
                .GetString()!);
            SpeechRecognitionMarkers.Add(document.RootElement
                .GetProperty("segment")
                .GetProperty("isSpeechRecognized")
                .GetBoolean());
            ApiKeys.Add(request.Headers.GetValues("X-Transcript-Adapter-Key").Single());
            return new HttpResponseMessage(
                fixedStatus
                ?? (SourceIds.Count == 1
                    ? HttpStatusCode.ServiceUnavailable
                    : HttpStatusCode.OK));
        }
    }

    private static CoachApiClient CreateClient(HttpMessageHandler handler, int maxAttempts) =>
        new(
            new HttpClient(handler),
            Options.Create(new CoachApiOptions
            {
                BaseUrl = new Uri("https://coach.example/"),
                AuthenticationMode = EndpointAuthenticationMode.DevelopmentApiKey,
                DevelopmentApiKey = new string('k', 32),
                MaxAttempts = maxAttempts
            }),
            new FakeCredential());

    private sealed class TimeoutOnceHandler : HttpMessageHandler
    {
        public int Attempts { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Attempts++;
            return Attempts == 1
                ? Task.FromException<HttpResponseMessage>(new TaskCanceledException("HTTP timeout"))
                : Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }

    private sealed class FakeCredential : TokenCredential
    {
        public override AccessToken GetToken(
            TokenRequestContext requestContext,
            CancellationToken cancellationToken) =>
            new("token", DateTimeOffset.UtcNow.AddHours(1));

        public override ValueTask<AccessToken> GetTokenAsync(
            TokenRequestContext requestContext,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(GetToken(requestContext, cancellationToken));
    }
}
