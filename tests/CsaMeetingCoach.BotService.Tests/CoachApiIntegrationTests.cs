using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.Core;
using CsaMeetingCoach.Api;
using CsaMeetingCoach.BotService.Configuration;
using CsaMeetingCoach.BotService.Integration;
using CsaMeetingCoach.BotService.Media;
using CsaMeetingCoach.Contracts;
using CsaMeetingCoach.Core;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace CsaMeetingCoach.BotService.Tests;

public sealed class CoachApiIntegrationTests
{
    private static readonly Uri TestBaseAddress = new("https://localhost/");
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    [Fact]
    public async Task CorrectKeyPublishesOnceWhenSourceSegmentIsRepeated()
    {
        using var factory = new CoachApiFactory();
        using var apiClient = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = TestBaseAddress
        });
        var session = await CreateSessionAsync(apiClient, "meeting-1");
        var coach = CreateCoachClient(apiClient, CoachApiFactory.ApiKey);
        var sourceSegmentId = Guid.NewGuid();
        var transcript = Transcript("We agreed the next owner and deadline.");

        await coach.PublishAsync(
            session.Id,
            "meeting-1",
            sourceSegmentId,
            transcript,
            CancellationToken.None);
        var firstUpdate = await GetSessionAsync(apiClient, session.Id);

        await coach.PublishAsync(
            session.Id,
            "meeting-1",
            sourceSegmentId,
            transcript,
            CancellationToken.None);
        var duplicateUpdate = await GetSessionAsync(apiClient, session.Id);

        var persisted = Assert.Single(duplicateUpdate.Transcript);
        Assert.Equal(sourceSegmentId, persisted.SourceSegmentId);
        Assert.Equal(firstUpdate.Revision, duplicateUpdate.Revision);
    }

    [Fact]
    public async Task WrongApiKeyIsRejectedWithoutPersistingTranscript()
    {
        using var factory = new CoachApiFactory();
        using var apiClient = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = TestBaseAddress
        });
        var session = await CreateSessionAsync(apiClient, "meeting-1");
        var coach = CreateCoachClient(apiClient, new string('x', 32));

        var exception = await Assert.ThrowsAsync<HttpRequestException>(() =>
            coach.PublishAsync(
                session.Id,
                "meeting-1",
                Guid.NewGuid(),
                Transcript("This must not be persisted."),
                CancellationToken.None));

        Assert.Equal(HttpStatusCode.Unauthorized, exception.StatusCode);
        Assert.Empty((await GetSessionAsync(apiClient, session.Id)).Transcript);
    }

    [Fact]
    public async Task MeetingMismatchIsRejectedWithoutPersistingTranscript()
    {
        using var factory = new CoachApiFactory();
        using var apiClient = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = TestBaseAddress
        });
        var session = await CreateSessionAsync(apiClient, "meeting-1");
        var coach = CreateCoachClient(apiClient, CoachApiFactory.ApiKey);

        var exception = await Assert.ThrowsAsync<HttpRequestException>(() =>
            coach.PublishAsync(
                session.Id,
                "different-meeting",
                Guid.NewGuid(),
                Transcript("This must not be persisted."),
                CancellationToken.None));

        Assert.Equal(HttpStatusCode.Unauthorized, exception.StatusCode);
        Assert.Empty((await GetSessionAsync(apiClient, session.Id)).Transcript);
    }

    [Fact]
    public async Task TransientFailureRetriesSameSourceIdAndPersistsOnce()
    {
        using var factory = new CoachApiFactory();
        using var sessionClient = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = TestBaseAddress
        });
        var session = await CreateSessionAsync(sessionClient, "meeting-1");
        var transientFailure = new FailFirstAdapterRequestHandler();
        using var publishClient = factory.CreateDefaultClient(TestBaseAddress, transientFailure);
        var coach = CreateCoachClient(
            publishClient,
            CoachApiFactory.ApiKey,
            maxAttempts: 2);
        var sourceSegmentId = Guid.NewGuid();

        await coach.PublishAsync(
            session.Id,
            "meeting-1",
            sourceSegmentId,
            Transcript("Retry this final transcript."),
            CancellationToken.None);

        Assert.Equal(2, transientFailure.SourceSegmentIds.Count);
        Assert.All(
            transientFailure.SourceSegmentIds,
            observed => Assert.Equal(sourceSegmentId, observed));
        var persisted = Assert.Single(
            (await GetSessionAsync(sessionClient, session.Id)).Transcript);
        Assert.Equal(sourceSegmentId, persisted.SourceSegmentId);
    }

    [Fact]
    public async Task RecommendationStatusAndReopenEndpointsUpdateTheLivePlan()
    {
        using var factory = new CoachApiFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = TestBaseAddress
        });
        var session = await CreateSessionAsync(client, "meeting-recommendations");
        session = await AddTranscriptAsync(
            client,
            session.Id,
            "Can we agree an owner for the architecture assessment?");
        var recommendation = Assert.Single(session.RecommendedTasks);
        Assert.Equal(RecommendationStatus.Proposed, recommendation.Status);

        using var acceptResponse = await client.PostAsJsonAsync(
            $"/api/sessions/{session.Id:D}/recommendations/{recommendation.Id:D}/status",
            new { status = "accepted" });
        acceptResponse.EnsureSuccessStatusCode();
        var accepted = (await acceptResponse.Content.ReadFromJsonAsync<MeetingSessionState>(
            JsonOptions))!;
        var acceptedRecommendation = Assert.Single(accepted.RecommendedTasks);
        Assert.Equal(RecommendationStatus.Accepted, acceptedRecommendation.Status);
        Assert.NotNull(acceptedRecommendation.AcceptedAtUtc);

        var completed = await AddTranscriptAsync(
            client,
            session.Id,
            "We agreed the owner for the architecture assessment and will send it Friday.");
        var completedRecommendation = Assert.Single(
            completed.RecommendedTasks.Where(task => task.Id == recommendation.Id));
        Assert.Equal(RecommendationStatus.Completed, completedRecommendation.Status);
        Assert.NotNull(completedRecommendation.CompletedAtUtc);
        Assert.False(string.IsNullOrWhiteSpace(completedRecommendation.CompletionReason));
        Assert.NotEmpty(completedRecommendation.Evidence!);

        using var reopenResponse = await client.PostAsync(
            $"/api/sessions/{session.Id:D}/recommendations/{recommendation.Id:D}/reopen",
            content: null);
        reopenResponse.EnsureSuccessStatusCode();
        var reopened = (await reopenResponse.Content.ReadFromJsonAsync<MeetingSessionState>(
            JsonOptions))!;
        var reopenedRecommendation = Assert.Single(
            reopened.RecommendedTasks.Where(task => task.Id == recommendation.Id));
        Assert.Equal(RecommendationStatus.Accepted, reopenedRecommendation.Status);
        Assert.Null(reopenedRecommendation.CompletedAtUtc);
        Assert.Empty(reopenedRecommendation.Evidence!);

        var dismissSession = await CreateSessionAsync(client, "meeting-dismiss");
        dismissSession = await AddTranscriptAsync(
            client,
            dismissSession.Id,
            "Can we confirm the customer's priority before closing?");
        var recommendationToDismiss = Assert.Single(dismissSession.RecommendedTasks);
        using var dismissResponse = await client.PostAsJsonAsync(
            $"/api/sessions/{dismissSession.Id:D}/recommendations/{recommendationToDismiss.Id:D}/status",
            new { status = "dismissed" });
        dismissResponse.EnsureSuccessStatusCode();
        var dismissed = (await dismissResponse.Content.ReadFromJsonAsync<MeetingSessionState>(
            JsonOptions))!;
        var dismissedRecommendation = Assert.Single(dismissed.RecommendedTasks);
        Assert.Equal(RecommendationStatus.Dismissed, dismissedRecommendation.Status);
        Assert.Null(dismissedRecommendation.AcceptedAtUtc);
    }

    private static CoachApiClient CreateCoachClient(
        HttpClient httpClient,
        string apiKey,
        int maxAttempts = 4) =>
        new(
            httpClient,
            Options.Create(new CoachApiOptions
            {
                BaseUrl = TestBaseAddress,
                AuthenticationMode = EndpointAuthenticationMode.DevelopmentApiKey,
                DevelopmentApiKey = apiKey,
                MaxAttempts = maxAttempts
            }),
            new UnexpectedCredential());

    private static async Task<MeetingSessionState> CreateSessionAsync(
        HttpClient client,
        string meetingId)
    {
        var request = new CreateMeetingSessionRequest(
            new MeetingPurpose(
                "VBD",
                "Value-based delivery",
                "Agree on customer outcomes and next steps.",
                ["Confirm outcomes", "Agree next steps"]),
            TeamsOnlineMeetingId: meetingId);
        using var response = await client.PostAsJsonAsync(
            "/api/sessions",
            request,
            JsonOptions);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<MeetingSessionState>(JsonOptions))!;
    }

    private static async Task<MeetingSessionState> GetSessionAsync(
        HttpClient client,
        Guid sessionId)
    {
        using var response = await client.GetAsync($"/api/sessions/{sessionId:D}");
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<MeetingSessionState>(JsonOptions))!;
    }

    private static async Task<MeetingSessionState> AddTranscriptAsync(
        HttpClient client,
        Guid sessionId,
        string text)
    {
        using var response = await client.PostAsJsonAsync(
            $"/api/sessions/{sessionId:D}/transcript",
            new AddTranscriptSegmentRequest("CSA", text),
            JsonOptions);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<MeetingSessionState>(JsonOptions))!;
    }

    private static FinalTranscript Transcript(string text) =>
        new(Guid.NewGuid().ToString("N"), text, DateTimeOffset.UtcNow);

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }

    private sealed class FailFirstAdapterRequestHandler : DelegatingHandler
    {
        public List<Guid> SourceSegmentIds { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (request.RequestUri?.AbsolutePath.Contains(
                    "/api/adapter/",
                    StringComparison.Ordinal) == true)
            {
                var originalContent = request.Content!;
                var contentBytes = await originalContent.ReadAsByteArrayAsync(cancellationToken);
                var replacementContent = new ByteArrayContent(contentBytes);
                foreach (var header in originalContent.Headers)
                {
                    replacementContent.Headers.TryAddWithoutValidation(header.Key, header.Value);
                }

                originalContent.Dispose();
                request.Content = replacementContent;
                var payload = JsonSerializer.Deserialize<AdapterTranscriptSegmentRequest>(
                    contentBytes,
                    JsonOptions);
                SourceSegmentIds.Add(payload!.Segment.SourceSegmentId!.Value);
                if (SourceSegmentIds.Count == 1)
                {
                    return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
                }
            }

            return await base.SendAsync(request, cancellationToken);
        }
    }

    private sealed class UnexpectedCredential : TokenCredential
    {
        public override AccessToken GetToken(
            TokenRequestContext requestContext,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Entra authentication was not expected.");

        public override ValueTask<AccessToken> GetTokenAsync(
            TokenRequestContext requestContext,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Entra authentication was not expected.");
    }
}

public sealed class CoachApiFactory : WebApplicationFactory<ApiEntryPoint>
{
    public const string ApiKey = "integration-test-adapter-key-0001";
    private readonly string testRoot = Path.Combine(
        Path.GetTempPath(),
        "CsaMeetingCoach",
        Guid.NewGuid().ToString("N"));

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        var dataDirectory = Path.Combine(testRoot, "data");
        var keyDirectory = Path.Combine(testRoot, "data-protection");
        Directory.CreateDirectory(keyDirectory);

        builder.UseEnvironment("IntegrationTests");
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IMeetingSessionStore>();
            services.AddSingleton<IMeetingSessionStore>(
                new JsonMeetingSessionStore(dataDirectory));
            services.RemoveAll<ISessionJoinCodeStore>();
            services.AddSingleton<ISessionJoinCodeStore>(
                new JsonSessionJoinCodeStore(dataDirectory));
            services.RemoveAll<IKnowledgeMalwareScanner>();
            services.AddSingleton<IKnowledgeMalwareScanner, AlwaysCleanKnowledgeScanner>();
            services.RemoveAll<ISessionKnowledgeStore>();
            services.RemoveAll<ISessionKnowledgeReader>();
            services.RemoveAll<ISessionArtifactCleaner>();
            services.AddSingleton<ISessionKnowledgeStore>(serviceProvider =>
                new LocalSessionKnowledgeStore(
                    Path.Combine(testRoot, "knowledge"),
                    serviceProvider.GetRequiredService<IKnowledgeMalwareScanner>()));
            services.AddSingleton<ISessionKnowledgeReader>(
                serviceProvider =>
                    serviceProvider.GetRequiredService<ISessionKnowledgeStore>());
            services.AddSingleton<ISessionArtifactCleaner>(
                serviceProvider =>
                    serviceProvider.GetRequiredService<ISessionKnowledgeStore>());
            services.RemoveAll<TranscriptAdapterAuthOptions>();
            services.AddSingleton(new TranscriptAdapterAuthOptions(
                TranscriptAdapterAuthMode.DevelopmentApiKey,
                ApiKey,
                "TranscriptIngestor"));
            services.AddDataProtection()
                .PersistKeysToFileSystem(new DirectoryInfo(keyDirectory))
                .SetApplicationName("CsaMeetingCoach.IntegrationTests");
        });
    }

    protected override void ConfigureClient(HttpClient client)
    {
        base.ConfigureClient(client);
        client.DefaultRequestHeaders.Add("X-Session-Request", "1");
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing && Directory.Exists(testRoot))
        {
            Directory.Delete(testRoot, recursive: true);
        }
    }

    private sealed class AlwaysCleanKnowledgeScanner : IKnowledgeMalwareScanner
    {
        public Task<KnowledgeMalwareScanResult> ScanAsync(
            string path,
            CancellationToken cancellationToken) =>
            Task.FromResult(KnowledgeMalwareScanResult.Clean);
    }
}
