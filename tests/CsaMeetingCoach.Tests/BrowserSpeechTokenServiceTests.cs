using System.Net;
using CsaMeetingCoach.Api;

namespace CsaMeetingCoach.Tests;

public sealed class BrowserSpeechTokenServiceTests
{
    [Fact]
    public void TokenResponse_PreservesFourArgumentConstructorCompatibility()
    {
        var now = DateTimeOffset.UtcNow;

        var response = new BrowserSpeechTokenResponse(
            "token",
            "westus2",
            "en-US",
            now);

        Assert.Empty(response.Phrases);
        Assert.Null(response.EndpointId);
    }

    [Fact]
    public async Task EnabledServiceIssuesShortLivedTokenWithoutRedirectingCredentials()
    {
        var handler = new RecordingHandler(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("  temporary-token  ")
            });
        var now = new DateTimeOffset(2026, 7, 31, 16, 0, 0, TimeSpan.Zero);
        var service = CreateService(handler, EnabledOptions(), now);

        var result = await service.IssueTokenAsync(CancellationToken.None);

        Assert.Equal("temporary-token", result.Token);
        Assert.Equal("westus2", result.Region);
        Assert.Equal("en-US", result.Language);
        Assert.Equal(now.AddMinutes(9), result.ExpiresAtUtc);
        Assert.Equal(
            new Uri("https://westus2.api.cognitive.microsoft.com/sts/v1.0/issueToken"),
            handler.RequestUri);
        Assert.Equal("speech-key", handler.SubscriptionKey);
        Assert.Equal(HttpMethod.Post, handler.Method);
    }

    [Fact]
    public async Task EnabledServiceIncludesSpeechPhraseVocabularyInToken()
    {
        var handler = new RecordingHandler(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("test-token")
            });
        var service = CreateService(handler, EnabledOptions(), DateTimeOffset.UtcNow);

        var result = await service.IssueTokenAsync(CancellationToken.None);

        Assert.NotNull(result.Phrases);
        Assert.NotEmpty(result.Phrases);
        Assert.True(result.Phrases.Count <= 500);
        Assert.Contains("Azure", result.Phrases, StringComparer.Ordinal);
        Assert.Null(result.EndpointId);
    }

    [Fact]
    public async Task ConfiguredCustomEndpointIsIncludedInTokenResponse()
    {
        const string endpointId = "6d58cb46-9d80-4d6b-8b8c-32a16742e018";
        var handler = new RecordingHandler(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("test-token")
            });
        var options = EnabledOptions(endpointId: endpointId);
        options.Validate();
        var service = CreateService(handler, options, DateTimeOffset.UtcNow);

        var result = await service.IssueTokenAsync(CancellationToken.None);

        Assert.Equal(endpointId, result.EndpointId);
    }

    [Fact]
    public async Task DisabledServiceFailsClosedWithoutCallingAzure()
    {
        var handler = new RecordingHandler(
            new HttpResponseMessage(HttpStatusCode.OK));
        var service = CreateService(
            handler,
            new BrowserSpeechOptions(),
            DateTimeOffset.UtcNow);

        await Assert.ThrowsAsync<BrowserSpeechUnavailableException>(() =>
            service.IssueTokenAsync(CancellationToken.None));
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task AzureFailureDoesNotExposeResponseOrSubscriptionKey()
    {
        var handler = new RecordingHandler(
            new HttpResponseMessage(HttpStatusCode.Forbidden)
            {
                Content = new StringContent("upstream-sensitive-detail")
            });
        var service = CreateService(
            handler,
            EnabledOptions(),
            DateTimeOffset.UtcNow);

        var exception = await Assert.ThrowsAsync<BrowserSpeechUnavailableException>(() =>
            service.IssueTokenAsync(CancellationToken.None));

        Assert.Contains("403", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("speech-key", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "upstream-sensitive-detail",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void OptionsRejectUnsafeRegionBeforeItCanBecomeARequestHost()
    {
        var options = EnabledOptions();
        options = new BrowserSpeechOptions
        {
            Enabled = true,
            SubscriptionKey = options.SubscriptionKey,
            Region = "westus2.example.com",
            Language = options.Language
        };

        Assert.Throws<InvalidOperationException>(options.Validate);
    }

    [Theory]
    [InlineData("not-a-guid")]
    [InlineData("6D58CB46-9D80-4D6B-8B8C-32A16742E018")]
    [InlineData("{6d58cb46-9d80-4d6b-8b8c-32a16742e018}")]
    [InlineData(" 6d58cb46-9d80-4d6b-8b8c-32a16742e018")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public void EnabledOptionsRejectNonCanonicalEndpointIds(string endpointId)
    {
        var options = EnabledOptions(endpointId: endpointId);

        var exception = Assert.Throws<InvalidOperationException>(options.Validate);

        Assert.Contains("canonical GUID", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EnabledOptionsAllowEmptyEndpointId()
    {
        var options = EnabledOptions(endpointId: string.Empty);

        options.Validate();
    }

    private static AzureBrowserSpeechTokenService CreateService(
        HttpMessageHandler handler,
        BrowserSpeechOptions options,
        DateTimeOffset now) =>
        new(
            new HttpClient(handler),
            options,
            new FixedTimeProvider(now));

    private static BrowserSpeechOptions EnabledOptions(
        string endpointId = "") =>
        new()
        {
            Enabled = true,
            SubscriptionKey = "speech-key",
            Region = "westus2",
            Language = "en-US",
            EndpointId = endpointId
        };

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class RecordingHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        public int CallCount { get; private set; }
        public Uri? RequestUri { get; private set; }
        public string? SubscriptionKey { get; private set; }
        public HttpMethod? Method { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            RequestUri = request.RequestUri;
            Method = request.Method;
            SubscriptionKey = Assert.Single(
                request.Headers.GetValues("Ocp-Apim-Subscription-Key"));
            return Task.FromResult(response);
        }
    }
}
