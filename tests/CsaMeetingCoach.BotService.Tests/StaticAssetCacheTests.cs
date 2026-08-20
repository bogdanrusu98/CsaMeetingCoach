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
        Assert.Contains("styles.css?v=20260820a", html, StringComparison.Ordinal);
        Assert.Contains("app.js?v=20260820a", html, StringComparison.Ordinal);
        Assert.Contains("id=\"toast-root\"", html, StringComparison.Ordinal);
        Assert.Contains(
            "vendor/react-toastify.bundle.js?v=11.1.0-20260819c",
            html,
            StringComparison.Ordinal);
        Assert.Contains(
            "vendor/react-toastify.bundle.css?v=11.1.0-20260819c",
            html,
            StringComparison.Ordinal);
        Assert.Contains("param || \"dark\"", html, StringComparison.Ordinal);
        Assert.Contains("data-theme", html, StringComparison.Ordinal);
        Assert.Contains("id=\"microphone-toggle\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"include-system-audio\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"microphone-access-status\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"recommendations\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"live-plan\"", html, StringComparison.Ordinal);
        Assert.Contains("Host a session", html, StringComparison.Ordinal);
        Assert.Contains("Join as member", html, StringComparison.Ordinal);
        Assert.Contains("Session knowledge", html, StringComparison.Ordinal);
        Assert.Contains("Host guidance", html, StringComparison.Ordinal);
        Assert.Contains("Evidence-backed plan", html, StringComparison.Ordinal);
        Assert.Contains("id=\"member-session-view\"", html, StringComparison.Ordinal);
        Assert.Contains("data-template-option=\"presentation\"", html, StringComparison.Ordinal);
        Assert.Contains("data-template-option=\"csaVbd\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"custom-meeting-type-field\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"host-participant-list\"", html, StringComparison.Ordinal);
        Assert.Contains("class=\"header-session-access\"", html, StringComparison.Ordinal);
        Assert.Contains("class=\"host-primary-row\"", html, StringComparison.Ordinal);
        Assert.Contains(
            "aria-label=\"Session controls and member experience\"",
            html,
            StringComparison.Ordinal);
        Assert.Contains("id=\"knowledge-file-form\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"knowledge-link-form\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"knowledge-file-visibility\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"knowledge-link-visibility\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"accepted-recommendations\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"checklist\"", html, StringComparison.Ordinal);
        Assert.Contains("Open diagnostics and transcript simulator", html, StringComparison.Ordinal);
        Assert.DoesNotContain("value=\"Host\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Cloud modernization workshop", html, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Align participants on the target outcome",
            html,
            StringComparison.Ordinal);
        Assert.Contains("id=\"diagnostics-dialog\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"speech-diagnostics\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("id=\"contextual-cards\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"contextual-card-history\"", html, StringComparison.Ordinal);
        Assert.Contains(
            "vendor/microsoft.cognitiveservices.speech.sdk.bundle-min.js?v=1.51.0",
            html,
            StringComparison.Ordinal);

        using var scriptResponse = await client.GetAsync("/app.js?v=20260820a");

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
        Assert.Contains("window.sessionToast", script, StringComparison.Ordinal);
        Assert.Contains("\"recommendation\"", script, StringComparison.Ordinal);
        Assert.Contains("/api/sessions/host", script, StringComparison.Ordinal);
        Assert.Contains("/api/sessions/join", script, StringComparison.Ordinal);
        Assert.Contains("/knowledge/files", script, StringComparison.Ordinal);
        Assert.Contains("X-Session-Request", script, StringComparison.Ordinal);
        Assert.Contains("function scheduleSessionExpiry", script, StringComparison.Ordinal);
        Assert.Contains("async function handleSessionExpired", script, StringComparison.Ordinal);
        Assert.Contains("sessionExpiryTimer", script, StringComparison.Ordinal);
        Assert.Contains(
            "addEventListener(\"expired\"",
            script,
            StringComparison.Ordinal);
        Assert.DoesNotContain("data-contextual-card-dismiss", script, StringComparison.Ordinal);
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
        Assert.Contains("const templateProfiles", script, StringComparison.Ordinal);
        Assert.Contains("renderParticipantPresence", script, StringComparison.Ordinal);
        Assert.Contains("joined the session.", script, StringComparison.Ordinal);
        Assert.Contains("window.scrollTo(0, 0)", script, StringComparison.Ordinal);
        Assert.Contains("Workshop facilitation", script, StringComparison.Ordinal);
        Assert.Contains("Training guidance", script, StringComparison.Ordinal);
        Assert.Contains("customMeetingType.required", script, StringComparison.Ordinal);

        using var styleResponse = await client.GetAsync("/styles.css?v=20260820a");
        styleResponse.EnsureSuccessStatusCode();
        AssertNoStore(styleResponse);
        var styles = await styleResponse.Content.ReadAsStringAsync();
        Assert.Contains("\"Segoe UI Variable Text\"", styles, StringComparison.Ordinal);
        Assert.Contains(".session-toast--error", styles, StringComparison.Ordinal);
        Assert.Contains(".session-toast--recommendation", styles, StringComparison.Ordinal);
        Assert.Contains(".session-toast--hint", styles, StringComparison.Ordinal);
        Assert.Contains(".session-toast--definition", styles, StringComparison.Ordinal);
        Assert.Contains(".role-card", styles, StringComparison.Ordinal);
        Assert.Contains(".host-layout", styles, StringComparison.Ordinal);
        Assert.Contains(".host-primary-row", styles, StringComparison.Ordinal);
        Assert.Contains(".header-session-access", styles, StringComparison.Ordinal);
        Assert.Contains(".guidance-privacy", styles, StringComparison.Ordinal);
        Assert.Contains("@media (max-width: 1180px)", styles, StringComparison.Ordinal);
        Assert.Contains("@media (max-width: 1080px)", styles, StringComparison.Ordinal);
        Assert.Contains(
            ".commercial-session-bar #connection-status.connected",
            styles,
            StringComparison.Ordinal);
        Assert.Contains(".member-alert-card", styles, StringComparison.Ordinal);
        Assert.Contains(".speech-diagnostics", styles, StringComparison.Ordinal);
        Assert.Contains(".template-option", styles, StringComparison.Ordinal);
        Assert.Contains("[data-template=\"training\"]", styles, StringComparison.Ordinal);
        Assert.Contains(".participant-list", styles, StringComparison.Ordinal);

        using var toastScriptResponse = await client.GetAsync(
            "/vendor/react-toastify.bundle.js?v=11.1.0-20260819c");
        toastScriptResponse.EnsureSuccessStatusCode();
        AssertNoStore(toastScriptResponse);
        var toastScript = await toastScriptResponse.Content.ReadAsStringAsync();
        Assert.Contains("sessionToast", toastScript, StringComparison.Ordinal);

        using var toastStyleResponse = await client.GetAsync(
            "/vendor/react-toastify.bundle.css?v=11.1.0-20260819c");
        toastStyleResponse.EnsureSuccessStatusCode();
        AssertNoStore(toastStyleResponse);

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
