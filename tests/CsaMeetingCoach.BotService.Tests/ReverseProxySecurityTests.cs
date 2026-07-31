using System.Net.Http.Json;
using CsaMeetingCoach.Contracts;
using Microsoft.AspNetCore.Mvc.Testing;

namespace CsaMeetingCoach.BotService.Tests;

public sealed class ReverseProxySecurityTests
{
    [Fact]
    public async Task TrustedHttpsForwardingCreatesSecureSessionCookie()
    {
        using var factory = new CoachApiFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("http://localhost/")
        });
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/sessions")
        {
            Content = JsonContent.Create(new CreateMeetingSessionRequest(
                new MeetingPurpose(
                    "VBD",
                    "Value-based delivery",
                    "Agree outcomes and next steps.",
                    ["Confirm outcomes", "Agree next steps"])))
        };
        request.Headers.TryAddWithoutValidation("X-Forwarded-For", "203.0.113.10");
        request.Headers.TryAddWithoutValidation("X-Forwarded-Proto", "https");

        using var response = await client.SendAsync(request);

        response.EnsureSuccessStatusCode();
        var sessionCookie = Assert.Single(
            response.Headers.GetValues("Set-Cookie"),
            value => value.StartsWith(
                "CsaMeetingCoach.Session.",
                StringComparison.Ordinal));
        Assert.Contains("; secure", sessionCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("; samesite=none", sessionCookie, StringComparison.OrdinalIgnoreCase);
    }
}
