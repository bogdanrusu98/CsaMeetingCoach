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
        Assert.Contains("styles.css?v=20260801", html, StringComparison.Ordinal);
        Assert.Contains("app.js?v=20260801", html, StringComparison.Ordinal);
        Assert.Contains("id=\"recommendations\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"live-plan\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"accepted-recommendations\"", html, StringComparison.Ordinal);
        Assert.Contains("What to discuss next", html, StringComparison.Ordinal);
        Assert.Contains("Your live plan", html, StringComparison.Ordinal);
        Assert.Contains("Start live coaching", html, StringComparison.Ordinal);
        Assert.Contains("not emotion or tone", html, StringComparison.Ordinal);
        Assert.Contains("<details class=\"panel diagnostics simulator\">", html, StringComparison.Ordinal);
        Assert.Contains(
            "vendor/microsoft.cognitiveservices.speech.sdk.bundle-min.js?v=1.51.0",
            html,
            StringComparison.Ordinal);

        using var scriptResponse = await client.GetAsync("/app.js?v=20260801");

        scriptResponse.EnsureSuccessStatusCode();
        AssertNoStore(scriptResponse);

        using var styleResponse = await client.GetAsync("/styles.css?v=20260801");
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
