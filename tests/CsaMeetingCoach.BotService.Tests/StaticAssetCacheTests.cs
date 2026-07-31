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
        Assert.Contains("app.js?v=20260731.1", html, StringComparison.Ordinal);

        using var scriptResponse = await client.GetAsync("/app.js?v=20260731.1");

        scriptResponse.EnsureSuccessStatusCode();
        AssertNoStore(scriptResponse);
    }

    private static void AssertNoStore(HttpResponseMessage response)
    {
        var cacheControl = Assert.Single(response.Headers.GetValues("Cache-Control"));
        Assert.Contains("no-store", cacheControl, StringComparison.OrdinalIgnoreCase);
    }
}
