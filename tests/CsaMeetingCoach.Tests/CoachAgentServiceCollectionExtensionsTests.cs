using Azure.Core;
using CsaMeetingCoach.Api;
using CsaMeetingCoach.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CsaMeetingCoach.Tests;

public sealed class CoachAgentServiceCollectionExtensionsTests
{
    [Fact]
    public void AddCoachAgent_Foundry_UsesSuppliedTokenCredential()
    {
        var configuration = CreateFoundryConfiguration();
        var credential = new TestTokenCredential();
        var services = new ServiceCollection();

        services.AddCoachAgent(configuration, credential);

        using var provider = services.BuildServiceProvider();
        Assert.Same(credential, provider.GetRequiredService<TokenCredential>());
        Assert.IsType<AzureFoundryAgentClient>(
            provider.GetRequiredService<IFoundryAgentClient>());
        Assert.IsType<FoundryConversationCoachAgent>(
            provider.GetRequiredService<IConversationCoachAgent>());
        var options = provider.GetRequiredService<FoundryOptions>();
        Assert.Equal("gpt-4.1-mini", options.ModelDeployment);
        Assert.Equal("csa-meeting-coach-v3", options.AgentName);
    }

    [Fact]
    public void AddCoachAgent_FoundryWithoutAgentName_RejectsStartup()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["CoachAgent:Provider"] = "Foundry",
                ["CoachAgent:Foundry:ProjectEndpoint"] =
                    "https://example.services.ai.azure.com/api/projects/example",
                ["CoachAgent:Foundry:ModelDeployment"] = "gpt-4.1-mini"
            })
            .Build();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            new ServiceCollection().AddCoachAgent(
                configuration,
                new TestTokenCredential()));

        Assert.Contains("CoachAgent:Foundry:AgentName", exception.Message);
    }

    [Fact]
    public void AddCoachAgent_FoundryWithNonHttpsEndpoint_RejectsStartup()
    {
        var configuration = CreateFoundryConfiguration(
            "http://example.services.ai.azure.com/api/projects/example");

        var exception = Assert.Throws<InvalidOperationException>(() =>
            new ServiceCollection().AddCoachAgent(
                configuration,
                new TestTokenCredential()));

        Assert.Contains("absolute HTTPS URI", exception.Message);
    }

    private static IConfiguration CreateFoundryConfiguration(
        string projectEndpoint =
            "https://example.services.ai.azure.com/api/projects/example") =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["CoachAgent:Provider"] = "Foundry",
                ["CoachAgent:Foundry:ProjectEndpoint"] = projectEndpoint,
                ["CoachAgent:Foundry:ModelDeployment"] = "gpt-4.1-mini",
                ["CoachAgent:Foundry:AgentName"] = "csa-meeting-coach-v3"
            })
            .Build();

    private sealed class TestTokenCredential : TokenCredential
    {
        public override AccessToken GetToken(
            TokenRequestContext requestContext,
            CancellationToken cancellationToken) =>
            new("test-token", DateTimeOffset.UtcNow.AddMinutes(5));

        public override ValueTask<AccessToken> GetTokenAsync(
            TokenRequestContext requestContext,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(GetToken(requestContext, cancellationToken));
    }
}
