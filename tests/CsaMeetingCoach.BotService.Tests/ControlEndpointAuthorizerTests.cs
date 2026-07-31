using CsaMeetingCoach.BotService.Configuration;
using CsaMeetingCoach.BotService.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace CsaMeetingCoach.BotService.Tests;

public sealed class ControlEndpointAuthorizerTests
{
    [Fact]
    public void DevelopmentKeyRequiresExactValue()
    {
        var expected = new string('s', 32);
        var authorizer = new ControlEndpointAuthorizer(Options.Create(
            new ControlEndpointOptions
            {
                AuthenticationMode = EndpointAuthenticationMode.DevelopmentApiKey,
                DevelopmentApiKey = expected
            }));
        var context = new DefaultHttpContext();
        context.Request.Headers["X-Media-Bot-Key"] = expected;

        Assert.True(authorizer.IsAuthorized(context));

        context.Request.Headers["X-Media-Bot-Key"] = $"{expected}x";
        Assert.False(authorizer.IsAuthorized(context));
    }
}
