using Azure.Core;
using Azure.Identity;
using CsaMeetingCoach.BotService.Configuration;
using CsaMeetingCoach.BotService.Graph;
using CsaMeetingCoach.BotService.Integration;
using CsaMeetingCoach.BotService.Media;
using CsaMeetingCoach.BotService.Security;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<BotOptionsValidator>();
builder.Services.AddSingleton<IValidateOptions<GraphOptions>>(services =>
    services.GetRequiredService<BotOptionsValidator>());
builder.Services.AddSingleton<IValidateOptions<MediaOptions>>(services =>
    services.GetRequiredService<BotOptionsValidator>());
builder.Services.AddSingleton<IValidateOptions<SpeechOptions>>(services =>
    services.GetRequiredService<BotOptionsValidator>());
builder.Services.AddSingleton<IValidateOptions<CoachApiOptions>>(services =>
    services.GetRequiredService<BotOptionsValidator>());
builder.Services.AddSingleton<IValidateOptions<ControlEndpointOptions>>(services =>
    services.GetRequiredService<BotOptionsValidator>());

builder.Services.AddOptions<GraphOptions>()
    .BindConfiguration(GraphOptions.SectionName)
    .ValidateOnStart();
builder.Services.AddOptions<MediaOptions>()
    .BindConfiguration(MediaOptions.SectionName)
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddOptions<SpeechOptions>()
    .BindConfiguration(SpeechOptions.SectionName)
    .ValidateOnStart();
builder.Services.AddOptions<CoachApiOptions>()
    .BindConfiguration(CoachApiOptions.SectionName)
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddOptions<ControlEndpointOptions>()
    .BindConfiguration(ControlEndpointOptions.SectionName)
    .ValidateOnStart();

var controlMode = builder.Configuration.GetValue<EndpointAuthenticationMode>(
    "ControlEndpoint:AuthenticationMode");
if (controlMode == EndpointAuthenticationMode.Entra)
{
    var tenantId = builder.Configuration["ControlEndpoint:TenantId"];
    var audience = builder.Configuration["ControlEndpoint:Audience"];
    builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
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
                RoleClaimType = "roles"
            };
        });
}

builder.Services.AddAuthorization();
builder.Services.AddSingleton<TokenCredential>(_ => new DefaultAzureCredential());
builder.Services.AddHttpClient<CoachApiClient>((services, client) =>
{
    var options = services.GetRequiredService<IOptions<CoachApiOptions>>().Value;
    client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
});
builder.Services.AddSingleton<ICoachApiClient>(services =>
    services.GetRequiredService<CoachApiClient>());
builder.Services.AddSingleton<ISpeechSessionFactory, AzureSpeechSessionFactory>();
builder.Services.AddSingleton<IGraphCallClient, GraphCallClient>();
builder.Services.AddSingleton<PipelineMetrics>();
builder.Services.AddSingleton<MeetingCallCoordinator>();
builder.Services.AddSingleton<ControlEndpointAuthorizer>();

var app = builder.Build();

if (controlMode == EndpointAuthenticationMode.Entra)
{
    app.UseAuthentication();
}

app.UseAuthorization();

app.MapGet("/health", (
    IGraphCallClient graph,
    PipelineMetrics metrics) =>
{
    var pipeline = metrics.Snapshot();
    var degraded = pipeline.FramesDropped > 0
        || pipeline.FinalSegmentsDropped > 0
        || pipeline.PublishFailures > 0
        || pipeline.PipelineFailures > 0
        || pipeline.KeepAliveFailures > 0;
    var ready = graph.Ready;
    var body = new
    {
        status = !graph.Enabled ? "disabled" : degraded ? "degraded" : ready ? "ready" : "not-ready",
        configured = graph.Configured,
        enabled = graph.Enabled,
        ready,
        degraded,
        pipeline
    };
    return graph.Enabled && (!ready || degraded)
        ? Results.Json(body, statusCode: StatusCodes.Status503ServiceUnavailable)
        : Results.Ok(body);
});

app.MapPost("/bot/calling", async (
    HttpContext context,
    IGraphCallClient graph,
    CancellationToken cancellationToken) =>
{
    if (!graph.Enabled || !graph.Ready)
    {
        return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
    }

    using var sdkRequest = await ToHttpRequestMessageAsync(context.Request, cancellationToken);
    using var sdkResponse = await graph.ProcessNotificationAsync(sdkRequest, cancellationToken);
    context.Response.StatusCode = (int)sdkResponse.StatusCode;
    foreach (var header in sdkResponse.Headers)
    {
        context.Response.Headers[header.Key] = header.Value.ToArray();
    }

    if (sdkResponse.Content is not null)
    {
        foreach (var header in sdkResponse.Content.Headers)
        {
            context.Response.Headers[header.Key] = header.Value.ToArray();
        }

        await sdkResponse.Content.CopyToAsync(context.Response.Body, cancellationToken);
    }

    return Results.Empty;
});

app.MapPost("/bot/join", async (
    JoinMeetingRequest request,
    HttpContext context,
    IGraphCallClient graph,
    ControlEndpointAuthorizer authorizer,
    MeetingCallCoordinator coordinator,
    CancellationToken cancellationToken) =>
{
    if (!graph.Enabled)
    {
        return Results.Problem(
            "Graph communications are disabled.",
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    if (!graph.Ready)
    {
        return Results.Problem(
            "Graph communications are not ready.",
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    if (!authorizer.IsAuthorized(context))
    {
        return Results.Unauthorized();
    }

    var validationError = ValidateJoinRequest(request);
    if (validationError is not null)
    {
        return Results.ValidationProblem(validationError);
    }

    try
    {
        var callId = await coordinator.JoinAsync(request, cancellationToken);
        return Results.Accepted(value: new { callId });
    }
    catch (CallCapacityException exception)
    {
        return Results.Problem(
            exception.Message,
            statusCode: StatusCodes.Status429TooManyRequests);
    }
});

app.Run();

static Dictionary<string, string[]>? ValidateJoinRequest(JoinMeetingRequest request)
{
    var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
    AddRequired(errors, nameof(request.TenantId), request.TenantId);
    AddRequired(errors, nameof(request.ChatId), request.ChatId);
    AddRequired(errors, nameof(request.OrganizerObjectId), request.OrganizerObjectId);
    AddRequired(errors, nameof(request.TeamsOnlineMeetingId), request.TeamsOnlineMeetingId);
    if (request.CoachSessionId == Guid.Empty)
    {
        errors[nameof(request.CoachSessionId)] = ["A non-empty coach session ID is required."];
    }

    return errors.Count == 0 ? null : errors;
}

static void AddRequired(Dictionary<string, string[]> errors, string name, string value)
{
    if (string.IsNullOrWhiteSpace(value))
    {
        errors[name] = [$"{name} is required."];
    }
}

static async Task<HttpRequestMessage> ToHttpRequestMessageAsync(
    HttpRequest request,
    CancellationToken cancellationToken)
{
    var target = new Uri($"{request.Scheme}://{request.Host}{request.PathBase}{request.Path}{request.QueryString}");
    var message = new HttpRequestMessage(new HttpMethod(request.Method), target);
    foreach (var header in request.Headers)
    {
        if (!message.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray()))
        {
            message.Content ??= new StreamContent(request.Body);
            message.Content.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray());
        }
    }

    if (request.ContentLength.GetValueOrDefault() > 0 && message.Content is null)
    {
        message.Content = new StreamContent(request.Body);
    }

    await Task.CompletedTask;
    cancellationToken.ThrowIfCancellationRequested();
    return message;
}

public partial class Program;
