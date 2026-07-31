using System.Net;
using CsaMeetingCoach.Api;
using Microsoft.AspNetCore.Http;

namespace CsaMeetingCoach.Tests;

public sealed class BrowserSpeechTokenServiceTests
{
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
            AccessKey = options.AccessKey,
            Region = "westus2.example.com",
            Language = options.Language
        };

        Assert.Throws<InvalidOperationException>(options.Validate);
    }

    [Fact]
    public void AccessCodeAuthorizationUsesAnExactCredential()
    {
        var options = EnabledOptions();
        options.Validate();
        var authorizer = new BrowserSpeechAuthorizer(options);
        var missingContext = new DefaultHttpContext();
        var wrongContext = new DefaultHttpContext();
        wrongContext.Request.Headers[
            BrowserSpeechAuthorizer.ApiKeyHeaderName] = new string('x', 32);
        var validContext = new DefaultHttpContext();
        validContext.Request.Headers[
            BrowserSpeechAuthorizer.ApiKeyHeaderName] = options.AccessKey;

        Assert.Throws<UnauthorizedAccessException>(() =>
            authorizer.Authorize(missingContext));
        Assert.Throws<UnauthorizedAccessException>(() =>
            authorizer.Authorize(wrongContext));
        authorizer.Authorize(validContext);
    }

    [Fact]
    public void EnabledOptionsRequireAThirtyTwoCharacterAccessCode()
    {
        var options = new BrowserSpeechOptions
        {
            Enabled = true,
            SubscriptionKey = "speech-key",
            AccessKey = "too-short",
            Region = "westus2",
            Language = "en-US"
        };

        Assert.Throws<InvalidOperationException>(options.Validate);
    }

    private static AzureBrowserSpeechTokenService CreateService(
        HttpMessageHandler handler,
        BrowserSpeechOptions options,
        DateTimeOffset now) =>
        new(
            new HttpClient(handler),
            options,
            new FixedTimeProvider(now));

    private static BrowserSpeechOptions EnabledOptions() =>
        new()
        {
            Enabled = true,
            SubscriptionKey = "speech-key",
            AccessKey = "browser-speech-access-key-00000001",
            Region = "westus2",
            Language = "en-US"
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
