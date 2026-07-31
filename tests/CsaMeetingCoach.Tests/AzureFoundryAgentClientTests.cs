using CsaMeetingCoach.Api;

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
        Assert.NotNull(definition.TextOptions);
        Assert.NotNull(definition.TextOptions.TextFormat);
    }
}
