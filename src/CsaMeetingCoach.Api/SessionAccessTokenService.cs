using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;

namespace CsaMeetingCoach.Api;

public sealed class SessionAccessTokenService(IDataProtectionProvider dataProtectionProvider)
{
    private const string CookiePrefix = "CsaMeetingCoach.Session.";
    private readonly IDataProtector _protector = dataProtectionProvider.CreateProtector(
        "CsaMeetingCoach.SessionAccess.v1");

    public void GrantAccess(HttpContext context, Guid sessionId)
    {
        var token = _protector.Protect(sessionId.ToString("N"));
        context.Response.Cookies.Append(
            GetCookieName(sessionId),
            token,
            new CookieOptions
            {
                HttpOnly = true,
                IsEssential = true,
                Path = "/api/sessions",
                SameSite = context.Request.IsHttps
                    ? SameSiteMode.None
                    : SameSiteMode.Strict,
                Secure = context.Request.IsHttps,
                MaxAge = TimeSpan.FromHours(12)
            });
    }

    public bool HasAccess(HttpContext context, Guid sessionId)
    {
        if (!context.Request.Cookies.TryGetValue(
                GetCookieName(sessionId),
                out var protectedToken)
            || string.IsNullOrWhiteSpace(protectedToken))
        {
            return false;
        }

        try
        {
            var protectedSessionId = _protector.Unprotect(protectedToken);
            return string.Equals(
                protectedSessionId,
                sessionId.ToString("N"),
                StringComparison.Ordinal);
        }
        catch (CryptographicException)
        {
            return false;
        }
    }

    private static string GetCookieName(Guid sessionId)
    {
        return string.Concat(CookiePrefix, sessionId.ToString("N"));
    }
}
