using CsaMeetingCoach.BotService.Configuration;
using Microsoft.Extensions.Configuration;

namespace CsaMeetingCoach.BotService.Tests;

public sealed class BotOptionsValidatorTests
{
    [Fact]
    public void DisabledGraphAllowsSecretFreeStartup()
    {
        var validator = CreateValidator(enabled: false);

        Assert.True(validator.Validate(null, new GraphOptions()).Succeeded);
        Assert.True(validator.Validate(null, new SpeechOptions()).Succeeded);
        Assert.True(validator.Validate(null, new CoachApiOptions()).Succeeded);
        Assert.True(validator.Validate(null, new ControlEndpointOptions()).Succeeded);
    }

    [Fact]
    public void EnabledGraphRejectsIncompleteConfiguration()
    {
        var validator = CreateValidator(enabled: true);

        Assert.True(validator.Validate(null, new GraphOptions { Enabled = true }).Failed);
        Assert.True(validator.Validate(null, new MediaOptions()).Failed);
        Assert.True(validator.Validate(null, new SpeechOptions()).Failed);
        Assert.True(validator.Validate(null, new CoachApiOptions()).Failed);
        Assert.True(validator.Validate(null, new ControlEndpointOptions()).Failed);
    }

    private static BotOptionsValidator CreateValidator(bool enabled)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Graph:Enabled"] = enabled.ToString()
            })
            .Build();
        return new BotOptionsValidator(configuration);
    }
}
