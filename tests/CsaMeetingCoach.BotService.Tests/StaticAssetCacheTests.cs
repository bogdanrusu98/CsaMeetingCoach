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
        Assert.Contains("styles.css?v=20260810b", html, StringComparison.Ordinal);
        Assert.Contains("app.js?v=20260810b", html, StringComparison.Ordinal);
        Assert.Contains("data-theme", html, StringComparison.Ordinal);
        Assert.Contains("id=\"microphone-toggle\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"include-system-audio\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"microphone-access-status\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"recommendations\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"live-plan\"", html, StringComparison.Ordinal);
        Assert.Contains("Next up", html, StringComparison.Ordinal);
        Assert.Contains("Meeting progress", html, StringComparison.Ordinal);
        Assert.Contains("id=\"diagnostics-dialog\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"speech-diagnostics\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"contextual-cards\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"contextual-card-history\"", html, StringComparison.Ordinal);
        Assert.Contains(
            "vendor/microsoft.cognitiveservices.speech.sdk.bundle-min.js?v=1.51.0",
            html,
            StringComparison.Ordinal);

        using var scriptResponse = await client.GetAsync("/app.js?v=20260810b");

        scriptResponse.EnsureSuccessStatusCode();
        AssertNoStore(scriptResponse);
        var script = await scriptResponse.Content.ReadAsStringAsync();
        Assert.DoesNotContain("sessionStorage", script, StringComparison.Ordinal);
        Assert.DoesNotContain("navigator.clipboard", script, StringComparison.Ordinal);
        Assert.Contains("Device authorized", script, StringComparison.Ordinal);
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
            "Speech_SegmentationStrategy",
            script,
            StringComparison.Ordinal);
        Assert.Contains("\"Time\"", script, StringComparison.Ordinal);
        Assert.Contains("Interim speech received.", script, StringComparison.Ordinal);
        Assert.Contains("Final speech received.", script, StringComparison.Ordinal);
        Assert.Contains("Final speech queued for the Coach API.", script, StringComparison.Ordinal);
        Assert.Contains("Coach API publish succeeded.", script, StringComparison.Ordinal);
        Assert.Contains(
            "32 to 256 printable ASCII characters",
            script,
            StringComparison.Ordinal);
        Assert.Contains("getDisplayMedia", script, StringComparison.Ordinal);
        Assert.Contains("AudioConfig.fromStreamInput", script, StringComparison.Ordinal);
        Assert.Contains("createMediaStreamDestination", script, StringComparison.Ordinal);
        Assert.Contains("Share system audio", script, StringComparison.Ordinal);
        Assert.Contains("renderContextualCards", script, StringComparison.Ordinal);
        Assert.Contains("data-contextual-card-dismiss", script, StringComparison.Ordinal);
        var recognizerIndex = script.IndexOf(
            "new window.SpeechSDK.SpeechRecognizer",
            StringComparison.Ordinal);
        var speechConfigIndex = script.IndexOf(
            "window.SpeechSDK.SpeechConfig.fromAuthorizationToken",
            StringComparison.Ordinal);
        var endpointIndex = script.IndexOf(
            "speechConfig.endpointId = token.endpointId",
            StringComparison.Ordinal);
        var phraseListIndex = script.IndexOf(
            "window.SpeechSDK.PhraseListGrammar.fromRecognizer(recognizer)",
            StringComparison.Ordinal);
        var recognitionStartIndex = script.IndexOf(
            "await startContinuousRecognition(recognizer)",
            StringComparison.Ordinal);
        Assert.True(speechConfigIndex >= 0);
        Assert.True(endpointIndex > speechConfigIndex);
        Assert.True(recognizerIndex > endpointIndex);
        Assert.True(recognizerIndex >= 0);
        Assert.True(phraseListIndex > recognizerIndex);
        Assert.True(recognitionStartIndex > phraseListIndex);
        Assert.Contains("phraseList.addPhrases(token.phrases)", script, StringComparison.Ordinal);
        Assert.Contains("phraseList.setWeight(2.0)", script, StringComparison.Ordinal);
        Assert.Contains("Custom Speech language model active.", script, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Custom Speech endpoint ${token.endpointId}",
            script,
            StringComparison.Ordinal);
        Assert.Contains("isSpeechRecognized: true", script, StringComparison.Ordinal);
        Assert.DoesNotContain("Original recognition: ${", script, StringComparison.Ordinal);
        Assert.Contains(
            "Original Speech SDK recognition retained for traceability",
            script,
            StringComparison.Ordinal);
        Assert.Contains("protected access lasts up to 30 days", script, StringComparison.Ordinal);

        using var styleResponse = await client.GetAsync("/styles.css?v=20260810b");
        styleResponse.EnsureSuccessStatusCode();
        AssertNoStore(styleResponse);
        var styles = await styleResponse.Content.ReadAsStringAsync();
        Assert.Contains(".contextual-card-stack", styles, StringComparison.Ordinal);
        Assert.Contains(".contextual-card", styles, StringComparison.Ordinal);
        Assert.Contains(".speech-diagnostics", styles, StringComparison.Ordinal);

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
