using Microsoft.AspNetCore.Mvc.Testing;

namespace CsaMeetingCoach.BotService.Tests;

public sealed class StaticAssetCacheTests
{
    [Fact]
    public async Task BrowserAssetsAreVersionedAndNotCached()
    {
        using var factory = new CoachApiFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("http://localhost/")
        });

        using var indexResponse = await client.GetAsync("/");

        indexResponse.EnsureSuccessStatusCode();
        AssertNoStore(indexResponse);
        var html = await indexResponse.Content.ReadAsStringAsync();
        Assert.Contains("styles.css?v=20260801d", html, StringComparison.Ordinal);
        Assert.Contains("app.js?v=20260802d", html, StringComparison.Ordinal);
        Assert.Contains("data-theme", html, StringComparison.Ordinal);
        Assert.Contains("id=\"microphone-toggle\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"recommendations\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"live-plan\"", html, StringComparison.Ordinal);
        Assert.Contains("Next up", html, StringComparison.Ordinal);
        Assert.Contains("Meeting progress", html, StringComparison.Ordinal);
        Assert.Contains("id=\"diagnostics-dialog\"", html, StringComparison.Ordinal);
        Assert.Contains(
            "vendor/microsoft.cognitiveservices.speech.sdk.bundle-min.js?v=1.51.0",
            html,
            StringComparison.Ordinal);

        using var scriptResponse = await client.GetAsync("/app.js?v=20260802d");

        scriptResponse.EnsureSuccessStatusCode();
        AssertNoStore(scriptResponse);
        var script = await scriptResponse.Content.ReadAsStringAsync();
        Assert.DoesNotContain("sessionStorage", script, StringComparison.Ordinal);
        Assert.Contains(
            "/api/browser-speech/access",
            script,
            StringComparison.Ordinal);
        Assert.Contains(
            "Speech_SegmentationSilenceTimeoutMs",
            script,
            StringComparison.Ordinal);
        Assert.Contains(
            "Speech_SegmentationMaximumTimeMs",
            script,
            StringComparison.Ordinal);
        Assert.Contains(
            "32 to 256 printable ASCII characters",
            script,
            StringComparison.Ordinal);

        using var styleResponse = await client.GetAsync("/styles.css?v=20260801d");
        styleResponse.EnsureSuccessStatusCode();
        AssertNoStore(styleResponse);

        using var speechSdkResponse = await client.GetAsync(
            "/vendor/microsoft.cognitiveservices.speech.sdk.bundle-min.js?v=1.51.0");
        speechSdkResponse.EnsureSuccessStatusCode();
        AssertNoStore(speechSdkResponse);
    }

    private static void AssertNoStore(HttpResponseMessage response)
    {
        var cacheControl = Assert.Single(response.Headers.GetValues("Cache-Control"));
        Assert.Contains("no-store", cacheControl, StringComparison.OrdinalIgnoreCase);
    }
}
