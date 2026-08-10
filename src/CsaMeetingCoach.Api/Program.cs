using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using CsaMeetingCoach.Api;
using CsaMeetingCoach.Contracts;
using CsaMeetingCoach.Core;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseWindowsService(options => options.ServiceName = "CSA Meeting Coach API");
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(
        new JsonStringEnumConverter(
            JsonNamingPolicy.CamelCase,
            allowIntegerValues: false));
});
var dataProtection = builder.Services.AddDataProtection()
    .SetApplicationName("CsaMeetingCoach");
var dataProtectionKeyDirectory =
    builder.Configuration["DataProtection:KeyDirectory"];
if (!string.IsNullOrWhiteSpace(dataProtectionKeyDirectory))
{
    var absoluteKeyDirectory = Path.IsPathRooted(dataProtectionKeyDirectory)
        ? dataProtectionKeyDirectory
        : Path.GetFullPath(Path.Combine(
            builder.Environment.ContentRootPath,
            dataProtectionKeyDirectory));
    dataProtection.PersistKeysToFileSystem(new DirectoryInfo(absoluteKeyDirectory));
}
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders =
        ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
    options.KnownProxies.Add(IPAddress.Loopback);
    options.KnownProxies.Add(IPAddress.IPv6Loopback);
});
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(
        context => RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 120,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            }));
});
builder.Services.AddSingleton<IMeetingChecklistPlanner, MeetingChecklistPlanner>();
builder.Services.AddSingleton<SessionAccessTokenService>();
builder.Services.AddSingleton<SessionEventBroker>();
builder.Services.AddSingleton<ISessionUpdatePublisher>(
    services => services.GetRequiredService<SessionEventBroker>());

var browserSpeechOptions = new BrowserSpeechOptions
{
    Enabled = builder.Configuration.GetValue<bool>("BrowserSpeech:Enabled"),
    SubscriptionKey = builder.Configuration["BrowserSpeech:SubscriptionKey"] ?? string.Empty,
    AccessKey = builder.Configuration["BrowserSpeech:AccessKey"] ?? string.Empty,
    Region = builder.Configuration["BrowserSpeech:Region"] ?? string.Empty,
    Language = builder.Configuration["BrowserSpeech:Language"] ?? "en-US",
    EndpointId = builder.Configuration["BrowserSpeech:EndpointId"] ?? string.Empty
};
browserSpeechOptions.Validate();
builder.Services.AddSingleton(browserSpeechOptions);
builder.Services.AddSingleton<BrowserSpeechAuthorizer>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services
    .AddHttpClient<IBrowserSpeechTokenService, AzureBrowserSpeechTokenService>(client =>
    {
        client.Timeout = TimeSpan.FromSeconds(10);
    })
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
    {
        AllowAutoRedirect = false
    });

var dataDirectory = builder.Configuration["Storage:DataDirectory"];
if (string.IsNullOrWhiteSpace(dataDirectory))
{
    dataDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CsaMeetingCoach",
        "data");
}
else if (!Path.IsPathRooted(dataDirectory))
{
    dataDirectory = Path.GetFullPath(
        Path.Combine(builder.Environment.ContentRootPath, dataDirectory));
}

builder.Services.AddSingleton<IMeetingSessionStore>(
    new JsonMeetingSessionStore(dataDirectory));
var coachAgentProvider = builder.Configuration["CoachAgent:Provider"] ?? "Local";
builder.Services.AddCoachAgent(builder.Configuration);

builder.Services.AddSingleton<MeetingSessionCoordinator>(sp => new MeetingSessionCoordinator(
    sp.GetRequiredService<IMeetingSessionStore>(),
    sp.GetRequiredService<IMeetingChecklistPlanner>(),
    sp.GetRequiredService<HeuristicConversationCoachAgent>(),
    sp.GetService<EvidenceBackedConversationCoachAgent>(),
    sp.GetRequiredService<ISessionUpdatePublisher>(),
    sp.GetRequiredService<ILogger<MeetingSessionCoordinator>>(),
    sp.GetService<AnalysisOptions>()));

var adapterAuthModeValue =
    builder.Configuration["TranscriptAdapter:AuthenticationMode"] ?? "Disabled";
if (!Enum.TryParse<TranscriptAdapterAuthMode>(
        adapterAuthModeValue,
        ignoreCase: true,
        out var adapterAuthMode))
{
    throw new InvalidOperationException(
        $"Unsupported transcript adapter authentication mode '{adapterAuthModeValue}'.");
}

var adapterRequiredRole =
    builder.Configuration["TranscriptAdapter:Entra:RequiredRole"]
    ?? "TranscriptIngestor";
var adapterDevelopmentApiKey =
    builder.Configuration["TranscriptAdapter:DevelopmentApiKey"] ?? string.Empty;

if (adapterAuthMode == TranscriptAdapterAuthMode.DevelopmentApiKey
    && adapterDevelopmentApiKey.Length < 32)
{
    throw new InvalidOperationException(
        "TranscriptAdapter:DevelopmentApiKey must contain at least 32 characters.");
}

if (adapterAuthMode == TranscriptAdapterAuthMode.Entra)
{
    var tenantId = RequireConfiguration(
        builder.Configuration,
        "TranscriptAdapter:Entra:TenantId");
    var audience = RequireConfiguration(
        builder.Configuration,
        "TranscriptAdapter:Entra:Audience");

    builder.Services
        .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(options =>
        {
            options.Authority = $"https://login.microsoftonline.com/{tenantId}/v2.0";
            options.Audience = audience;
            options.MapInboundClaims = false;
            options.RequireHttpsMetadata = true;
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateAudience = true,
                ValidateIssuer = true,
                ValidateLifetime = true,
                RoleClaimType = "roles",
                NameClaimType = "azp"
            };
        });
}

builder.Services.AddAuthorization();
builder.Services.AddSingleton(new TranscriptAdapterAuthOptions(
    adapterAuthMode,
    adapterDevelopmentApiKey,
    adapterRequiredRole));
builder.Services.AddSingleton<TranscriptAdapterAuthorizer>();

var app = builder.Build();

app.UseForwardedHeaders();
app.UseExceptionHandler(errorApplication =>
{
    errorApplication.Run(async context =>
    {
        var exception = context.Features
            .Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>()
            ?.Error;

        var (status, title) = exception switch
        {
            ArgumentException => (StatusCodes.Status400BadRequest, "Invalid request"),
            BadHttpRequestException => (StatusCodes.Status400BadRequest, "Invalid request"),
            JsonException => (StatusCodes.Status400BadRequest, "Invalid JSON request"),
            UnauthorizedAccessException => (StatusCodes.Status401Unauthorized, "Access denied"),
            KeyNotFoundException => (StatusCodes.Status404NotFound, "Resource not found"),
            TranscriptAdapterUnavailableException => (
                StatusCodes.Status503ServiceUnavailable,
                "Transcript adapter unavailable"),
            BrowserSpeechUnavailableException => (
                StatusCodes.Status503ServiceUnavailable,
                "Browser speech unavailable"),
            InvalidOperationException => (StatusCodes.Status409Conflict, "Operation rejected"),
            _ => (StatusCodes.Status500InternalServerError, "Unexpected server error")
        };

        context.Response.StatusCode = status;
        context.Response.ContentType = "application/problem+json";
        await context.Response.WriteAsJsonAsync(new
        {
            type = "about:blank",
            title,
            status,
            detail = status == StatusCodes.Status500InternalServerError
                ? "The server could not process the request."
                : exception?.Message
        });
    });
});

app.Use((context, next) =>
{
    if (!context.Request.Path.StartsWithSegments("/api"))
    {
        context.Response.Headers.CacheControl =
            "no-store, no-cache, must-revalidate, max-age=0";
        context.Response.Headers.Pragma = "no-cache";
        context.Response.Headers.Expires = "0";
    }

    return next(context);
});
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseRateLimiter();
if (adapterAuthMode == TranscriptAdapterAuthMode.Entra)
{
    app.UseAuthentication();
}

app.UseAuthorization();

app.MapGet("/api/health", () => Results.Ok(new
{
    status = "healthy",
    coachAgentProvider,
    liveTranscriptSource = browserSpeechOptions.Enabled
        ? "Browser microphone/Simulator/API adapter"
        : "Simulator/API adapter",
    transcriptAdapterAuthentication = adapterAuthMode.ToString(),
    browserMicrophoneTranscription = browserSpeechOptions.Enabled ? "ready" : "disabled",
    browserSpeechModel = !browserSpeechOptions.Enabled
        ? "disabled"
        : string.IsNullOrEmpty(browserSpeechOptions.EndpointId)
            ? "base"
            : "custom"
}));

app.MapGet(
    "/api/browser-speech/access",
    (HttpContext context, BrowserSpeechAuthorizer speechAuthorizer) =>
        Results.Ok(new
        {
            authorized = speechAuthorizer.HasPersistentAccess(context)
        }));

app.MapPost(
    "/api/sessions",
    async (
        CreateMeetingSessionRequest request,
        HttpContext context,
        SessionAccessTokenService accessTokens,
        MeetingSessionCoordinator coordinator,
        CancellationToken cancellationToken) =>
    {
        var session = await coordinator.CreateAsync(request, cancellationToken);
        accessTokens.GrantAccess(context, session.Id);
        return Results.Created($"/api/sessions/{session.Id}", session);
    });

app.MapPost(
    "/api/sessions/{sessionId:guid}/speech-token",
    async (
        Guid sessionId,
        HttpContext context,
        SessionAccessTokenService accessTokens,
        MeetingSessionCoordinator coordinator,
        BrowserSpeechAuthorizer speechAuthorizer,
        IBrowserSpeechTokenService speechTokens,
        CancellationToken cancellationToken) =>
    {
        RequireSessionAccess(context, sessionId, accessTokens);
        var session = await coordinator.GetAsync(sessionId, cancellationToken)
            ?? throw new KeyNotFoundException($"Meeting session {sessionId} was not found.");
        if (session.Status != MeetingSessionStatus.Active)
        {
            throw new InvalidOperationException(
                "Microphone transcription requires an active meeting session.");
        }

        var shouldGrantPersistentAccess = speechAuthorizer.Authorize(context);
        var token = await speechTokens.IssueTokenAsync(cancellationToken);
        if (shouldGrantPersistentAccess)
        {
            speechAuthorizer.GrantPersistentAccess(context);
        }

        context.Response.Headers.CacheControl = "no-store";
        return Results.Ok(token);
    });

app.MapPost(
    "/api/adapter/sessions/{sessionId:guid}/transcript",
    async (
        Guid sessionId,
        AdapterTranscriptSegmentRequest request,
        HttpContext context,
        TranscriptAdapterAuthorizer adapterAuthorizer,
        MeetingSessionCoordinator coordinator,
        CancellationToken cancellationToken) =>
    {
        adapterAuthorizer.Authorize(context);

        if (!request.Segment.SourceSegmentId.HasValue
            || request.Segment.SourceSegmentId.Value == Guid.Empty)
        {
            throw new ArgumentException(
                "Adapter transcript segments require a non-empty source segment ID.");
        }

        var session = await coordinator.GetAsync(sessionId, cancellationToken)
            ?? throw new KeyNotFoundException($"Meeting session {sessionId} was not found.");
        if (string.IsNullOrWhiteSpace(session.TeamsOnlineMeetingId)
            || !string.Equals(
                session.TeamsOnlineMeetingId,
                request.TeamsOnlineMeetingId?.Trim(),
                StringComparison.Ordinal))
        {
            throw new UnauthorizedAccessException(
                "The transcript adapter meeting binding does not match this session.");
        }

        return Results.Ok(await coordinator.AddTranscriptAsync(
            sessionId,
            request.Segment,
            cancellationToken));
    });

app.MapGet(
    "/api/sessions/{sessionId:guid}",
    async (
        Guid sessionId,
        HttpContext context,
        SessionAccessTokenService accessTokens,
        MeetingSessionCoordinator coordinator,
        CancellationToken cancellationToken) =>
    {
        RequireSessionAccess(context, sessionId, accessTokens);
        var session = await coordinator.GetAsync(sessionId, cancellationToken);
        return session is null ? Results.NotFound() : Results.Ok(session);
    });

app.MapPost(
    "/api/sessions/{sessionId:guid}/transcript",
    async (
        Guid sessionId,
        AddTranscriptSegmentRequest request,
        HttpContext context,
        SessionAccessTokenService accessTokens,
        MeetingSessionCoordinator coordinator,
        CancellationToken cancellationToken) =>
    {
        RequireSessionAccess(context, sessionId, accessTokens);
        return Results.Ok(await coordinator.AddTranscriptAsync(
            sessionId,
            request,
            cancellationToken));
    });

app.MapPost(
    "/api/sessions/{sessionId:guid}/checklist/{checklistItemId:guid}/reopen",
    async (
        Guid sessionId,
        Guid checklistItemId,
        HttpContext context,
        SessionAccessTokenService accessTokens,
        MeetingSessionCoordinator coordinator,
        CancellationToken cancellationToken) =>
    {
        RequireSessionAccess(context, sessionId, accessTokens);
        return Results.Ok(await coordinator.ReopenChecklistItemAsync(
            sessionId,
            checklistItemId,
            cancellationToken));
    });

app.MapPost(
    "/api/sessions/{sessionId:guid}/recommendations/{recommendationId:guid}/status",
    async (
        Guid sessionId,
        Guid recommendationId,
        SetRecommendationStatusRequest request,
        HttpContext context,
        SessionAccessTokenService accessTokens,
        MeetingSessionCoordinator coordinator,
        CancellationToken cancellationToken) =>
    {
        RequireSessionAccess(context, sessionId, accessTokens);
        return Results.Ok(await coordinator.SetRecommendationStatusAsync(
            sessionId,
            recommendationId,
            request.Status,
            cancellationToken));
    });

app.MapPost(
    "/api/sessions/{sessionId:guid}/recommendations/{recommendationId:guid}/reopen",
    async (
        Guid sessionId,
        Guid recommendationId,
        HttpContext context,
        SessionAccessTokenService accessTokens,
        MeetingSessionCoordinator coordinator,
        CancellationToken cancellationToken) =>
    {
        RequireSessionAccess(context, sessionId, accessTokens);
        return Results.Ok(await coordinator.ReopenRecommendationAsync(
            sessionId,
            recommendationId,
            cancellationToken));
    });

app.MapPost(
    "/api/sessions/{sessionId:guid}/complete",
    async (
        Guid sessionId,
        HttpContext context,
        SessionAccessTokenService accessTokens,
        MeetingSessionCoordinator coordinator,
        CancellationToken cancellationToken) =>
    {
        RequireSessionAccess(context, sessionId, accessTokens);
        return Results.Ok(await coordinator.CompleteAsync(sessionId, cancellationToken));
    });

app.MapGet(
    "/api/sessions/{sessionId:guid}/events",
    async (
        Guid sessionId,
        HttpContext context,
        SessionAccessTokenService accessTokens,
        SessionEventBroker broker,
        MeetingSessionCoordinator coordinator,
        CancellationToken cancellationToken) =>
    {
        RequireSessionAccess(context, sessionId, accessTokens);
        await using var subscription = broker.Subscribe(sessionId);
        var currentSession = await coordinator.GetAsync(sessionId, cancellationToken);
        if (currentSession is null)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        context.Response.Headers.CacheControl = "no-cache";
        context.Response.Headers.Connection = "keep-alive";
        context.Response.ContentType = "text/event-stream";

        await context.Response.WriteAsync(": connected\n\n", cancellationToken);
        await context.Response.WriteAsync(
            $"event: session\ndata: {broker.SerializeSession(currentSession)}\n\n",
            cancellationToken);
        await context.Response.Body.FlushAsync(cancellationToken);
        await foreach (var payload in subscription.Reader.ReadAllAsync(cancellationToken))
        {
            await context.Response.WriteAsync(
                $"event: session\ndata: {payload}\n\n",
                cancellationToken);
            await context.Response.Body.FlushAsync(cancellationToken);
        }
    });

app.MapFallbackToFile("index.html");

app.Run();

static string RequireConfiguration(IConfiguration configuration, string key)
{
    var value = configuration[key];
    return string.IsNullOrWhiteSpace(value)
        ? throw new InvalidOperationException($"Configuration '{key}' is required.")
        : value;
}

static void RequireSessionAccess(
    HttpContext context,
    Guid sessionId,
    SessionAccessTokenService accessTokens)
{
    if (!accessTokens.HasAccess(context, sessionId))
    {
        throw new UnauthorizedAccessException(
            "A valid access token for this meeting session is required.");
    }
}

namespace CsaMeetingCoach.Api
{
    public sealed record SetRecommendationStatusRequest(RecommendationStatus Status);
    public sealed class ApiEntryPoint;
}
