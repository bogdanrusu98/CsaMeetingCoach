using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using CsaMeetingCoach.Contracts;
using Microsoft.AspNetCore.Mvc.Testing;

namespace CsaMeetingCoach.BotService.Tests;

public sealed class BrowserSpeechEndpointTests
{
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    [Fact]
    public async Task DisabledSpeechFailsClosedForSessionOwner()
    {
        using var factory = new CoachApiFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost/")
        });
        var sessionId = await CreateSessionAsync(client);

        using var response = await client.PostAsync(
            $"/api/sessions/{sessionId:D}/speech-token",
            content: null);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(
            "Browser microphone transcription is not configured.",
            problem.GetProperty("detail").GetString());
        Assert.DoesNotContain(
            "SubscriptionKey",
            await response.Content.ReadAsStringAsync(),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task SpeechTokenRequiresTheSessionAccessCookie()
    {
        using var factory = new CoachApiFactory();
        using var owner = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost/")
        });
        using var stranger = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost/")
        });
        var sessionId = await CreateSessionAsync(owner);

        using var response = await stranger.PostAsync(
            $"/api/sessions/{sessionId:D}/speech-token",
            content: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static async Task<Guid> CreateSessionAsync(HttpClient client)
    {
        using var response = await client.PostAsJsonAsync(
            "/api/sessions",
            new CreateMeetingSessionRequest(
                new MeetingPurpose(
                    "Microphone test",
                    "VBD",
                    "Validate explicit browser speech authorization.",
                    ["Capture final consented speech segments"])),
            JsonOptions);
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        return payload.GetProperty("id").GetGuid();
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }
}
