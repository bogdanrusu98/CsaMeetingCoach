using System.Net.Http.Json;
using System.Text.Json;
using CsaMeetingCoach.Contracts;
using Microsoft.AspNetCore.Mvc.Testing;

namespace CsaMeetingCoach.BotService.Tests;

public sealed class ServerSentEventTests
{
    [Fact]
    public async Task EventStreamStartsWithTheCurrentSession()
    {
        using var factory = new CoachApiFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("http://localhost/")
        });
        var request = new CreateMeetingSessionRequest(
            new MeetingPurpose(
                "SSE test",
                "VBD",
                "Validate the live connection.",
                ["Connect to the event stream"]));
        using var createResponse = await client.PostAsJsonAsync("/api/sessions", request);
        createResponse.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(
            await createResponse.Content.ReadAsStringAsync());
        var sessionId = document.RootElement.GetProperty("id").GetGuid();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        using var response = await client.GetAsync(
            $"/api/sessions/{sessionId:D}/events",
            HttpCompletionOption.ResponseHeadersRead,
            timeout.Token);

        response.EnsureSuccessStatusCode();
        Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);
        await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
        using var reader = new StreamReader(stream);
        var session = await ReadInitialSessionAsync(reader, timeout.Token);

        Assert.Equal(sessionId, session.GetProperty("id").GetGuid());
        Assert.Equal("active", session.GetProperty("status").GetString());
        Assert.Equal(1, session.GetProperty("revision").GetInt64());
    }

    [Fact]
    public async Task EventStreamReportsCompletionThatOccurredBeforeSubscription()
    {
        using var factory = new CoachApiFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("http://localhost/")
        });
        var request = new CreateMeetingSessionRequest(
            new MeetingPurpose(
                "SSE reconnect test",
                "VBD",
                "Stop local capture after a missed completion event.",
                ["Reconnect safely"]));
        using var createResponse = await client.PostAsJsonAsync("/api/sessions", request);
        createResponse.EnsureSuccessStatusCode();
        using var createdDocument = JsonDocument.Parse(
            await createResponse.Content.ReadAsStringAsync());
        var sessionId = createdDocument.RootElement.GetProperty("id").GetGuid();
        using var completeResponse = await client.PostAsync(
            $"/api/sessions/{sessionId:D}/complete",
            content: null);
        completeResponse.EnsureSuccessStatusCode();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        using var response = await client.GetAsync(
            $"/api/sessions/{sessionId:D}/events",
            HttpCompletionOption.ResponseHeadersRead,
            timeout.Token);

        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
        using var reader = new StreamReader(stream);
        var session = await ReadInitialSessionAsync(reader, timeout.Token);

        Assert.Equal(sessionId, session.GetProperty("id").GetGuid());
        Assert.Equal("completed", session.GetProperty("status").GetString());
        Assert.Equal(2, session.GetProperty("revision").GetInt64());
    }

    private static async Task<JsonElement> ReadInitialSessionAsync(
        StreamReader reader,
        CancellationToken cancellationToken)
    {
        Assert.Equal(": connected", await reader.ReadLineAsync(cancellationToken));
        Assert.Equal(string.Empty, await reader.ReadLineAsync(cancellationToken));
        Assert.Equal("event: session", await reader.ReadLineAsync(cancellationToken));
        var data = await reader.ReadLineAsync(cancellationToken);
        Assert.NotNull(data);
        Assert.StartsWith("data: ", data, StringComparison.Ordinal);
        using var document = JsonDocument.Parse(data["data: ".Length..]);
        return document.RootElement.Clone();
    }
}
