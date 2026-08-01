using CsaMeetingCoach.Api;
using CsaMeetingCoach.Core;

namespace CsaMeetingCoach.Tests;

public sealed class AzureFoundryAgentClientTests
{
    [Fact]
    public void BuildResponseOptions_LeavesStructuredOutputOnAgent()
    {
#pragma warning disable OPENAI001
        var options = AzureFoundryAgentClient.BuildResponseOptions("{}");

        Assert.Null(options.TextOptions);
        Assert.Equal(2_000, options.MaxOutputTokenCount);
        Assert.Single(options.InputItems);
#pragma warning restore OPENAI001
    }

    [Fact]
    public void BuildAgentDefinition_ConfiguresStrictStructuredOutput()
    {
        var definition = AzureFoundryAgentClient.BuildAgentDefinition(
            "gpt-4.1-mini");

        Assert.Equal("gpt-4.1-mini", definition.Model);
        Assert.Empty(definition.Tools);
        Assert.NotNull(definition.TextOptions);
        Assert.NotNull(definition.TextOptions.TextFormat);
        Assert.Contains(
            "recommendationEvaluations",
            FoundryAgentContract.ResponseJsonSchema,
            StringComparison.Ordinal);
        Assert.Contains(
            "what the CSA should discuss",
            FoundryAgentContract.Instructions,
            StringComparison.Ordinal);
        Assert.Contains(
            "Meeting dialogue is normally direct and first-person",
            FoundryAgentContract.Instructions,
            StringComparison.Ordinal);
        Assert.Contains(
            "must never count as proof",
            FoundryAgentContract.Instructions,
            StringComparison.Ordinal);
        Assert.Contains(
            "exact evidenceQuote from",
            FoundryAgentContract.Instructions,
            StringComparison.Ordinal);
        Assert.Contains(
            "Deterministic approval remains authoritative",
            FoundryAgentContract.Instructions,
            StringComparison.Ordinal);
    }

    [Fact]
    public void BuildAgentDefinition_WithVectorStore_AddsFileSearchTool()
    {
        var definition = AzureFoundryAgentClient.BuildAgentDefinition(
            "gpt-4.1-mini",
            ["vs_abc123"]);

        Assert.Single(definition.Tools);
    }

    [Fact]
    public void BuildAgentUpdateBody_IncludesReviewedVectorStore()
    {
        var body = AzureFoundryAgentClient.BuildAgentUpdateBody(
            "gpt-4.1-mini",
            ["vs_reviewed"]);
        var json = body.ToString();

        Assert.Contains("\"file_search\"", json, StringComparison.Ordinal);
        Assert.Contains("\"vs_reviewed\"", json, StringComparison.Ordinal);
        Assert.Contains(
            "\"model\":\"gpt-4.1-mini\"",
            json,
            StringComparison.Ordinal);
    }
}
