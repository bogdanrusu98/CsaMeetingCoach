using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CsaMeetingCoach.Contracts;
using Microsoft.AspNetCore.Mvc.Testing;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;

namespace CsaMeetingCoach.BotService.Tests;

public sealed class CommercialSessionEndpointTests
{
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    [Fact]
    public async Task HostCreatesAndMemberJoinsWithRoleScopedView()
    {
        using var factory = new CoachApiFactory();
        using var host = CreateClient(factory);
        using var member = CreateClient(factory);
        var created = await CreateHostSessionAsync(host);

        using var joinResponse = await member.PostAsJsonAsync(
            "/api/sessions/join",
            new JoinMeetingSessionRequest(created.JoinCode, "Member"),
            JsonOptions);
        joinResponse.EnsureSuccessStatusCode();
        using var memberResponse = await member.GetAsync(
            $"/api/sessions/{created.Session.Id:D}");
        memberResponse.EnsureSuccessStatusCode();
        Assert.True(memberResponse.Headers.CacheControl?.NoStore);
        Assert.True(memberResponse.Headers.CacheControl?.Private);
        Assert.Contains(
            memberResponse.Headers.Vary,
            value => string.Equals(value, "Cookie", StringComparison.OrdinalIgnoreCase));
        using var memberJson = JsonDocument.Parse(
            await memberResponse.Content.ReadAsStringAsync());

        Assert.Equal(created.Session.Id, memberJson.RootElement.GetProperty("id").GetGuid());
        Assert.True(memberJson.RootElement.TryGetProperty("alerts", out _));
        Assert.False(memberJson.RootElement.TryGetProperty("transcript", out _));
        Assert.False(memberJson.RootElement.TryGetProperty("recommendedTasks", out _));
        Assert.False(memberJson.RootElement.TryGetProperty("participants", out _));
        Assert.False(memberJson.RootElement.TryGetProperty("knowledgeSources", out _));

        using var refreshedHostResponse = await host.GetAsync(
            $"/api/sessions/{created.Session.Id:D}");
        refreshedHostResponse.EnsureSuccessStatusCode();
        var refreshedHost = await refreshedHostResponse.Content
            .ReadFromJsonAsync<MeetingSessionState>(JsonOptions);
        Assert.NotNull(refreshedHost);
        var joinedMember = Assert.Single(
            refreshedHost.Participants,
            participant => participant.Role == SessionRole.Member);
        Assert.Equal("Member", joinedMember.DisplayName);

        using var forbidden = await member.PostAsJsonAsync(
            $"/api/sessions/{created.Session.Id:D}/transcript",
            new AddTranscriptSegmentRequest("Member", "I must not write transcript."),
            JsonOptions);
        Assert.Equal(HttpStatusCode.Unauthorized, forbidden.StatusCode);
    }

    [Fact]
    public async Task HostPrivateKnowledgeRequiresApprovalBeforeMemberAlertIsVisible()
    {
        using var factory = new CoachApiFactory();
        using var host = CreateClient(factory);
        using var member = CreateClient(factory);
        var created = await CreateHostSessionAsync(host);
        using var joinResponse = await member.PostAsJsonAsync(
            "/api/sessions/join",
            new JoinMeetingSessionRequest(created.JoinCode, "Member"),
            JsonOptions);
        joinResponse.EnsureSuccessStatusCode();

        using var upload = new MultipartFormDataContent();
        var file = new ByteArrayContent(Encoding.UTF8.GetBytes(
            "Azure Load Balancer distributes layer four traffic across healthy backends."));
        file.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        upload.Add(file, "file", "load-balancer.txt");
        upload.Add(new StringContent("hostPrivate"), "visibility");
        using var uploadRequest = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/sessions/{created.Session.Id:D}/knowledge/files")
        {
            Content = upload
        };
        uploadRequest.Headers.Add("X-Session-Request", "1");
        using var uploadResponse = await host.SendAsync(uploadRequest);
        uploadResponse.EnsureSuccessStatusCode();

        using var transcriptResponse = await host.PostAsJsonAsync(
            $"/api/sessions/{created.Session.Id:D}/transcript",
            new AddTranscriptSegmentRequest(
                "Host",
                "Azure Load Balancer distributes layer four traffic across backend resources."),
            JsonOptions);
        transcriptResponse.EnsureSuccessStatusCode();
        var hostState = (await transcriptResponse.Content.ReadFromJsonAsync<MeetingSessionState>(
            JsonOptions))!;
        var card = Assert.Single(hostState.ContextualCards);
        Assert.Equal(MemberAlertStatus.PendingApproval, card.MemberAlertStatus);

        var beforeApproval = await GetMemberViewAsync(member, created.Session.Id);
        Assert.Empty(beforeApproval.Alerts);

        using var publishResponse = await host.PostAsJsonAsync(
            $"/api/sessions/{created.Session.Id:D}/alerts/{card.Id:D}/status",
            new { status = "published" });
        publishResponse.EnsureSuccessStatusCode();
        var afterApproval = await GetMemberViewAsync(member, created.Session.Id);
        Assert.Single(afterApproval.Alerts);
    }

    [Fact]
    public async Task InvalidJoinCodeReturnsGenericNotFound()
    {
        using var factory = new CoachApiFactory();
        using var client = CreateClient(factory);

        using var response = await client.PostAsJsonAsync(
            "/api/sessions/join",
            new JoinMeetingSessionRequest("2345-6789", "Member"),
            JsonOptions);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(
            "The session code is invalid or expired.",
            payload.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task HostCanUploadReadablePdfKnowledge()
    {
        using var factory = new CoachApiFactory();
        using var host = CreateClient(factory);
        var created = await CreateHostSessionAsync(host);
        var builder = new PdfDocumentBuilder();
        var page = builder.AddPage(PageSize.A4);
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        page.AddText(
            "Availability zones isolate datacenter-level failures.",
            12,
            new PdfPoint(40, 760),
            font);

        using var upload = new MultipartFormDataContent();
        var file = new ByteArrayContent(builder.Build());
        file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        upload.Add(file, "file", "availability.pdf");
        upload.Add(new StringContent("memberEligible"), "visibility");
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/sessions/{created.Session.Id:D}/knowledge/files")
        {
            Content = upload
        };
        request.Headers.Add("X-Session-Request", "1");

        using var response = await host.SendAsync(request);

        response.EnsureSuccessStatusCode();
        var session = (await response.Content.ReadFromJsonAsync<MeetingSessionState>(
            JsonOptions))!;
        var source = Assert.Single(session.KnowledgeSources);
        Assert.Equal("availability.pdf", source.DisplayName);
        Assert.Equal(KnowledgeSourceStatus.Ready, source.Status);
        Assert.Equal(KnowledgeSourceVisibility.MemberEligible, source.Visibility);
    }

    [Fact]
    public async Task BodylessHostMutationRequiresCsrfResistantHeader()
    {
        using var factory = new CoachApiFactory();
        using var host = CreateClient(factory);
        host.DefaultRequestHeaders.Remove("X-Session-Request");
        var created = await CreateHostSessionAsync(host);

        using var response = await host.PostAsync(
            $"/api/sessions/{created.Session.Id:D}/complete",
            content: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static HttpClient CreateClient(CoachApiFactory factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost/")
        });

    private static async Task<CreateHostSessionResponse> CreateHostSessionAsync(
        HttpClient client)
    {
        using var response = await client.PostAsJsonAsync(
            "/api/sessions/host",
            new CreateMeetingSessionRequest(
                new MeetingPurpose(
                    "Commercial session",
                    "Presentation",
                    "Explain the architecture.",
                    ["Explain traffic distribution"]),
                Template: SessionTemplateKind.CsaVbd,
                HostDisplayName: "Host"),
            JsonOptions);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<CreateHostSessionResponse>(
            JsonOptions))!;
    }

    private static async Task<MemberSessionView> GetMemberViewAsync(
        HttpClient client,
        Guid sessionId)
    {
        using var response = await client.GetAsync($"/api/sessions/{sessionId:D}");
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<MemberSessionView>(JsonOptions))!;
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }
}
