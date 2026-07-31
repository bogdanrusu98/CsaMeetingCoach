using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Options;

namespace CsaMeetingCoach.BotService.Configuration;

public enum EndpointAuthenticationMode
{
    Disabled,
    DevelopmentApiKey,
    Entra
}

public sealed class GraphOptions
{
    public const string SectionName = "Graph";
    public bool Enabled { get; init; }
    public string TenantId { get; init; } = string.Empty;
    public string AppId { get; init; } = string.Empty;
    public string ClientSecret { get; init; } = string.Empty;
    public Uri? NotificationUrl { get; init; }
    public Uri ServiceBaseUrl { get; init; } = new("https://graph.microsoft.com/v1.0");
}

public sealed class MediaOptions
{
    public const string SectionName = "Media";
    public string ServiceFqdn { get; init; } = string.Empty;
    public string PublicIpAddress { get; init; } = string.Empty;
    [Range(1, 65535)] public int PublicPort { get; init; } = 8445;
    [Range(1, 65535)] public int InternalPort { get; init; } = 8445;
    public string CertificateThumbprint { get; init; } = string.Empty;
    [Range(1, 500)] public int AudioQueueCapacity { get; init; } = 100;
    [Range(1, 500)] public int TranscriptQueueCapacity { get; init; } = 100;
    [Range(1, 1000)] public int MaxConcurrentCalls { get; init; } = 20;
    [Range(5, 300)] public int EstablishmentTimeoutSeconds { get; init; } = 60;
    [Range(1, 30)] public int KeepAliveIntervalMinutes { get; init; } = 15;
}

public sealed class SpeechOptions
{
    public const string SectionName = "Speech";
    public string SubscriptionKey { get; init; } = string.Empty;
    public string Region { get; init; } = string.Empty;
    public string Language { get; init; } = "en-US";
}

public sealed class CoachApiOptions
{
    public const string SectionName = "CoachApi";
    public Uri? BaseUrl { get; init; }
    public EndpointAuthenticationMode AuthenticationMode { get; init; }
    public string DevelopmentApiKey { get; init; } = string.Empty;
    public string EntraScope { get; init; } = string.Empty;
    [Range(1, 10)] public int MaxAttempts { get; init; } = 4;
    [Range(1, 120)] public int TimeoutSeconds { get; init; } = 15;
}

public sealed class ControlEndpointOptions
{
    public const string SectionName = "ControlEndpoint";
    public EndpointAuthenticationMode AuthenticationMode { get; init; }
    public string DevelopmentApiKey { get; init; } = string.Empty;
    public string TenantId { get; init; } = string.Empty;
    public string Audience { get; init; } = string.Empty;
    public string RequiredRole { get; init; } = "MediaBot.Controller";
}

public sealed class BotOptionsValidator :
    IValidateOptions<GraphOptions>,
    IValidateOptions<MediaOptions>,
    IValidateOptions<SpeechOptions>,
    IValidateOptions<CoachApiOptions>,
    IValidateOptions<ControlEndpointOptions>
{
    private readonly IConfiguration configuration;

    public BotOptionsValidator(IConfiguration configuration) => this.configuration = configuration;

    public ValidateOptionsResult Validate(string? name, GraphOptions options)
    {
        if (!options.Enabled)
        {
            return ValidateOptionsResult.Success;
        }

        return Required(
            (options.TenantId, "Graph:TenantId"),
            (options.AppId, "Graph:AppId"),
            (options.ClientSecret, "Graph:ClientSecret"),
            (options.NotificationUrl?.Scheme == Uri.UriSchemeHttps ? "ok" : string.Empty, "Graph:NotificationUrl (HTTPS)"),
            (options.ServiceBaseUrl.Scheme == Uri.UriSchemeHttps ? "ok" : string.Empty, "Graph:ServiceBaseUrl (HTTPS)"));
    }

    public ValidateOptionsResult Validate(string? name, MediaOptions options) =>
        GraphEnabled()
            ? Required(
                (options.ServiceFqdn, "Media:ServiceFqdn"),
                (System.Net.IPAddress.TryParse(options.PublicIpAddress, out _) ? "ok" : string.Empty, "Media:PublicIpAddress"),
                (options.CertificateThumbprint, "Media:CertificateThumbprint"))
            : ValidateOptionsResult.Success;

    public ValidateOptionsResult Validate(string? name, SpeechOptions options) =>
        GraphEnabled()
            ? Required(
                (options.SubscriptionKey, "Speech:SubscriptionKey"),
                (options.Region, "Speech:Region"),
                (options.Language, "Speech:Language"))
            : ValidateOptionsResult.Success;

    public ValidateOptionsResult Validate(string? name, CoachApiOptions options)
    {
        if (!GraphEnabled())
        {
            return ValidateOptionsResult.Success;
        }

        if (!Enum.IsDefined(options.AuthenticationMode))
        {
            return ValidateOptionsResult.Fail("CoachApi:AuthenticationMode is invalid.");
        }

        var common = Required(
            (options.BaseUrl?.Scheme == Uri.UriSchemeHttps ? "ok" : string.Empty, "CoachApi:BaseUrl (HTTPS)"),
            (options.AuthenticationMode == EndpointAuthenticationMode.Disabled ? string.Empty : "ok", "CoachApi:AuthenticationMode"));
        if (common.Failed)
        {
            return common;
        }

        return options.AuthenticationMode switch
        {
            EndpointAuthenticationMode.DevelopmentApiKey when options.DevelopmentApiKey.Length < 32 =>
                ValidateOptionsResult.Fail("CoachApi:DevelopmentApiKey must contain at least 32 characters."),
            EndpointAuthenticationMode.Entra when !options.EntraScope.EndsWith("/.default", StringComparison.Ordinal) =>
                ValidateOptionsResult.Fail("CoachApi:EntraScope must end with '/.default'."),
            _ => ValidateOptionsResult.Success
        };
    }

    public ValidateOptionsResult Validate(string? name, ControlEndpointOptions options)
    {
        if (!GraphEnabled())
        {
            return ValidateOptionsResult.Success;
        }

        if (!Enum.IsDefined(options.AuthenticationMode))
        {
            return ValidateOptionsResult.Fail("ControlEndpoint:AuthenticationMode is invalid.");
        }

        return options.AuthenticationMode switch
        {
            EndpointAuthenticationMode.DevelopmentApiKey when options.DevelopmentApiKey.Length >= 32 =>
                ValidateOptionsResult.Success,
            EndpointAuthenticationMode.DevelopmentApiKey =>
                ValidateOptionsResult.Fail("ControlEndpoint:DevelopmentApiKey must contain at least 32 characters."),
            EndpointAuthenticationMode.Entra => Required(
                (options.TenantId, "ControlEndpoint:TenantId"),
                (options.Audience, "ControlEndpoint:Audience"),
                (options.RequiredRole, "ControlEndpoint:RequiredRole")),
            _ => ValidateOptionsResult.Fail("ControlEndpoint authentication must be enabled when Graph is enabled.")
        };
    }

    private bool GraphEnabled() => configuration.GetValue<bool>("Graph:Enabled");

    private static ValidateOptionsResult Required(params (string? Value, string Name)[] values)
    {
        var missing = values.Where(value => string.IsNullOrWhiteSpace(value.Value)).Select(value => value.Name).ToArray();
        return missing.Length == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail($"Missing or invalid configuration: {string.Join(", ", missing)}.");
    }
}
