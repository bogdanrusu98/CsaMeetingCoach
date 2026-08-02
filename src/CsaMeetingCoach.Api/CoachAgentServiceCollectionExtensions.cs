using Azure.Core;
using Azure.Identity;
using CsaMeetingCoach.Core;
using System.Text.RegularExpressions;

namespace CsaMeetingCoach.Api;

public static partial class CoachAgentServiceCollectionExtensions
{
    public static IServiceCollection AddCoachAgent(
        this IServiceCollection services,
        IConfiguration configuration,
        TokenCredential? foundryCredential = null)
    {
        var provider = configuration["CoachAgent:Provider"] ?? "Local";
        services.AddSingleton<ISemanticTermCatalog, BuiltInSemanticTermCatalog>();
        services.AddSingleton<HeuristicConversationCoachAgent>();
        if (provider.Equals("Local", StringComparison.OrdinalIgnoreCase))
        {
            services.AddSingleton<IConversationCoachAgent>(serviceProvider =>
                serviceProvider.GetRequiredService<HeuristicConversationCoachAgent>());
            return services;
        }

        if (provider.Equals("AzureOpenAI", StringComparison.OrdinalIgnoreCase))
        {
            var endpoint = RequireConfiguration(
                configuration,
                "CoachAgent:AzureOpenAI:Endpoint");
            var deployment = RequireConfiguration(
                configuration,
                "CoachAgent:AzureOpenAI:Deployment");
            var apiVersion = RequireConfiguration(
                configuration,
                "CoachAgent:AzureOpenAI:ApiVersion");
            var apiKey = RequireConfiguration(
                configuration,
                "CoachAgent:AzureOpenAI:ApiKey");
            var options = new AzureOpenAiOptions(
                RequireHttpsUri(endpoint, "CoachAgent:AzureOpenAI:Endpoint"),
                deployment,
                apiVersion,
                apiKey);

            services.AddSingleton(new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(30)
            });
            services.AddSingleton<AzureOpenAiConversationCoachAgent>(serviceProvider =>
                new AzureOpenAiConversationCoachAgent(
                    serviceProvider.GetRequiredService<HttpClient>(),
                    options));
            services.AddSingleton<EvidenceBackedConversationCoachAgent>(sp =>
                new EvidenceBackedConversationCoachAgent(
                    sp.GetRequiredService<AzureOpenAiConversationCoachAgent>(),
                    sp.GetRequiredService<HeuristicConversationCoachAgent>()));
            services.AddSingleton<IConversationCoachAgent>(sp =>
                sp.GetRequiredService<EvidenceBackedConversationCoachAgent>());
            return services;
        }

        if (provider.Equals("Foundry", StringComparison.OrdinalIgnoreCase))
        {
            var endpoint = RequireConfiguration(
                configuration,
                "CoachAgent:Foundry:ProjectEndpoint");
            var modelDeployment = RequireConfiguration(
                configuration,
                "CoachAgent:Foundry:ModelDeployment");
            var agentName = RequireConfiguration(
                configuration,
                "CoachAgent:Foundry:AgentName");
            if (agentName.Length > 64)
            {
                throw new InvalidOperationException(
                    "Configuration 'CoachAgent:Foundry:AgentName' cannot exceed 64 characters.");
            }

            var vectorStoreIds = ParseVectorStoreIds(
                configuration["CoachAgent:Foundry:VectorStoreIds"]);
            if (vectorStoreIds.Count == 0)
            {
                throw new InvalidOperationException(
                    "Configuration 'CoachAgent:Foundry:VectorStoreIds' must contain at least one reviewed vector store ID.");
            }

            var options = new FoundryOptions(
                RequireHttpsUri(endpoint, "CoachAgent:Foundry:ProjectEndpoint"),
                modelDeployment,
                agentName,
                vectorStoreIds);
            services.AddSingleton(options);
            services.AddSingleton<TokenCredential>(
                foundryCredential ?? new DefaultAzureCredential());
            services.AddSingleton<AzureFoundryAgentClient>();
            services.AddSingleton<IFoundryAgentClient>(sp =>
                sp.GetRequiredService<AzureFoundryAgentClient>());
            services.AddSingleton<FoundryConversationCoachAgent>();
            services.AddSingleton<EvidenceBackedConversationCoachAgent>(sp =>
                new EvidenceBackedConversationCoachAgent(
                    sp.GetRequiredService<FoundryConversationCoachAgent>(),
                    sp.GetRequiredService<HeuristicConversationCoachAgent>()));
            services.AddSingleton<IConversationCoachAgent>(sp =>
                sp.GetRequiredService<EvidenceBackedConversationCoachAgent>());
            services.AddHostedService<FoundryWarmUpService>();
            return services;
        }

        throw new InvalidOperationException(
            $"Unsupported CoachAgent provider '{provider}'. Use Local, AzureOpenAI, or Foundry.");
    }

    private static string RequireConfiguration(
        IConfiguration configuration,
        string key)
    {
        var value = configuration[key];
        return string.IsNullOrWhiteSpace(value)
            ? throw new InvalidOperationException($"Configuration '{key}' is required.")
            : value.Trim();
    }

    private static Uri RequireHttpsUri(string value, string key)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || !uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(uri.Host)
            || !string.IsNullOrEmpty(uri.UserInfo))
        {
            throw new InvalidOperationException(
                $"Configuration '{key}' must be an absolute HTTPS URI without user information.");
        }

        return uri;
    }

    internal static IReadOnlyList<string> ParseVectorStoreIds(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        var ids = value.Split(',', StringSplitOptions.None)
            .Select(id => id.Trim())
            .ToArray();
        if (ids.Any(string.IsNullOrEmpty))
        {
            throw new InvalidOperationException(
                "Configuration 'CoachAgent:Foundry:VectorStoreIds' contains an empty identifier.");
        }

        var distinctIds = ids.Distinct(StringComparer.Ordinal).ToArray();
        if (distinctIds.Length > 10)
        {
            throw new InvalidOperationException(
                "Configuration 'CoachAgent:Foundry:VectorStoreIds' cannot contain more than 10 identifiers.");
        }

        if (distinctIds.Any(id => !VectorStoreIdRegex().IsMatch(id)))
        {
            throw new InvalidOperationException(
                "Configuration 'CoachAgent:Foundry:VectorStoreIds' must contain only valid vs_ identifiers.");
        }

        return Array.AsReadOnly(distinctIds);
    }

    [GeneratedRegex(
        @"^vs_[A-Za-z0-9]{1,125}$",
        RegexOptions.CultureInvariant)]
    private static partial Regex VectorStoreIdRegex();
}
