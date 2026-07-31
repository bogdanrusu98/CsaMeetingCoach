using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using CsaMeetingCoach.Contracts;
using Microsoft.AspNetCore.Mvc.Testing;

namespace CsaMeetingCoach.BotService.Tests;

public sealed class ServerSentEventTests
{
    [Fact]
    public async Task EventStreamOpensBeforeTheFirstSessionUpdate()
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
        var buffer = new byte[32];
        var bytesRead = await stream.ReadAsync(buffer, timeout.Token);
        var initialEvent = Encoding.UTF8.GetString(buffer, 0, bytesRead);
        Assert.Equal(": connected\n\n", initialEvent);
    }
}
