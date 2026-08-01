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
        Assert.IsType<EvidenceBackedConversationCoachAgent>(
            provider.GetRequiredService<IConversationCoachAgent>());
        var options = provider.GetRequiredService<FoundryOptions>();
        Assert.Equal("gpt-4.1-mini", options.ModelDeployment);
        Assert.Equal("csa-meeting-coach-v5", options.AgentName);
        Assert.Equal(["vs_reviewed"], options.VectorStoreIds);
        Assert.IsType<BuiltInSemanticTermCatalog>(
            provider.GetRequiredService<ISemanticTermCatalog>());
    }

    [Fact]
    public void AddCoachAgent_FoundryWithoutVectorStore_RejectsStartup()
    {
        var configuration = CreateFoundryConfiguration(vectorStoreIds: "");

        var exception = Assert.Throws<InvalidOperationException>(() =>
            new ServiceCollection().AddCoachAgent(
                configuration,
                new TestTokenCredential()));

        Assert.Contains("at least one reviewed vector store ID", exception.Message);
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

    [Fact]
    public void AddCoachAgent_Foundry_ParsesAndDeduplicatesVectorStoreIds()
    {
        var configuration = CreateFoundryConfiguration(
            vectorStoreIds: " vs_alpha,vs_beta,vs_alpha ");
        var services = new ServiceCollection();

        services.AddCoachAgent(configuration, new TestTokenCredential());

        using var provider = services.BuildServiceProvider();
        Assert.Equal(
            ["vs_alpha", "vs_beta"],
            provider.GetRequiredService<FoundryOptions>().VectorStoreIds);
    }

    [Theory]
    [InlineData("not-a-store")]
    [InlineData("vs_good,")]
    [InlineData("vs_has-hyphen")]
    [InlineData("vs_good,,vs_other")]
    public void AddCoachAgent_Foundry_RejectsMalformedVectorStoreIds(
        string vectorStoreIds)
    {
        var configuration = CreateFoundryConfiguration(
            vectorStoreIds: vectorStoreIds);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            new ServiceCollection().AddCoachAgent(
                configuration,
                new TestTokenCredential()));

        Assert.Contains("VectorStoreIds", exception.Message);
    }

    [Fact]
    public void AddCoachAgent_Foundry_RejectsTooManyVectorStoreIds()
    {
        var ids = string.Join(
            ',',
            Enumerable.Range(0, 11).Select(index => $"vs_store{index}"));

        var exception = Assert.Throws<InvalidOperationException>(() =>
            new ServiceCollection().AddCoachAgent(
                CreateFoundryConfiguration(vectorStoreIds: ids),
                new TestTokenCredential()));

        Assert.Contains("more than 10", exception.Message);
    }

    private static IConfiguration CreateFoundryConfiguration(
        string projectEndpoint =
            "https://example.services.ai.azure.com/api/projects/example",
        string? vectorStoreIds = "vs_reviewed") =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["CoachAgent:Provider"] = "Foundry",
                ["CoachAgent:Foundry:ProjectEndpoint"] = projectEndpoint,
                ["CoachAgent:Foundry:ModelDeployment"] = "gpt-4.1-mini",
                ["CoachAgent:Foundry:AgentName"] = "csa-meeting-coach-v5",
                ["CoachAgent:Foundry:VectorStoreIds"] = vectorStoreIds
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
