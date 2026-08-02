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
        var normalizedInstructions = string.Join(
            ' ',
            FoundryAgentContract.Instructions.Split(
                (char[]?)null,
                StringSplitOptions.RemoveEmptyEntries));

        Assert.Equal("gpt-4.1-mini", definition.Model);
        Assert.Empty(definition.Tools);
        Assert.NotNull(definition.TextOptions);
        Assert.NotNull(definition.TextOptions.TextFormat);
        Assert.Contains(
            "recommendationEvaluations",
            FoundryAgentContract.ResponseJsonSchema,
            StringComparison.Ordinal);
        Assert.Contains(
            "\"maxItems\": 1",
            FoundryAgentContract.ResponseJsonSchema,
            StringComparison.Ordinal);
        Assert.Contains(
            "what the CSA should discuss",
            normalizedInstructions,
            StringComparison.Ordinal);
        Assert.Contains(
            "Meeting dialogue is normally direct and first-person",
            normalizedInstructions,
            StringComparison.Ordinal);
        Assert.Contains(
            "must never count as proof",
            normalizedInstructions,
            StringComparison.Ordinal);
        Assert.Contains(
            "exact evidenceQuote from",
            normalizedInstructions,
            StringComparison.Ordinal);
        Assert.Contains(
            "Deterministic approval remains authoritative",
            normalizedInstructions,
            StringComparison.Ordinal);
        Assert.Contains(
            "use file search before naming candidates",
            normalizedInstructions,
            StringComparison.Ordinal);
        Assert.Contains(
            "technical fit from commercial eligibility",
            normalizedInstructions,
            StringComparison.Ordinal);
        Assert.Contains(
            "Never invent or present unverified pricing",
            normalizedInstructions,
            StringComparison.Ordinal);
        Assert.Contains(
            "Do not upsell",
            normalizedInstructions,
            StringComparison.Ordinal);
        Assert.Contains(
            "recommend a focused clarification or assessment",
            normalizedInstructions,
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
