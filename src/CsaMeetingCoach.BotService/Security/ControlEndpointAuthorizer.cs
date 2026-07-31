using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using CsaMeetingCoach.BotService.Configuration;
using Microsoft.Extensions.Options;

namespace CsaMeetingCoach.BotService.Security;

public sealed class ControlEndpointAuthorizer(IOptions<ControlEndpointOptions> options)
{
    private readonly ControlEndpointOptions options = options.Value;

    public bool IsAuthorized(HttpContext context)
    {
        if (options.AuthenticationMode == EndpointAuthenticationMode.DevelopmentApiKey)
        {
            var provided = context.Request.Headers["X-Media-Bot-Key"].ToString();
            return FixedTimeEquals(provided, options.DevelopmentApiKey);
        }

        return context.User.Identity?.IsAuthenticated == true
            && context.User.FindAll("roles").Any(claim =>
                string.Equals(claim.Value, options.RequiredRole, StringComparison.Ordinal));
    }

    private static bool FixedTimeEquals(string provided, string expected)
    {
        var left = Encoding.UTF8.GetBytes(provided);
        var right = Encoding.UTF8.GetBytes(expected);
        return left.Length == right.Length
            && CryptographicOperations.FixedTimeEquals(left, right);
    }
}
