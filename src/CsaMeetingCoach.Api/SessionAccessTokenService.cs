using System.Security.Cryptography;
using System.Text.Json;
using CsaMeetingCoach.Contracts;
using CsaMeetingCoach.Core;
using Microsoft.AspNetCore.DataProtection;

namespace CsaMeetingCoach.Api;

public sealed class SessionAccessTokenService(
    IDataProtectionProvider dataProtectionProvider,
    TimeProvider timeProvider)
{
    private const string CookiePrefix = "CsaMeetingCoach.Session.";
    private readonly IDataProtector _protector = dataProtectionProvider.CreateProtector(
        "CsaMeetingCoach.SessionAccess.v2");

    public void GrantAccess(HttpContext context, SessionAccessGrant grant)
    {
        if (grant.ExpiresAtUtc <= timeProvider.GetUtcNow())
        {
            throw new SessionExpiredException(grant.SessionId);
        }

        var token = _protector.Protect(JsonSerializer.Serialize(grant));
        context.Response.Cookies.Append(
            GetCookieName(grant.SessionId),
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
                MaxAge = grant.ExpiresAtUtc - timeProvider.GetUtcNow()
            });
    }

    public bool TryGetAccess(
        HttpContext context,
        Guid sessionId,
        out SessionAccessGrant grant)
    {
        grant = default!;
        if (!context.Request.Cookies.TryGetValue(
                GetCookieName(sessionId),
                out var protectedToken)
            || string.IsNullOrWhiteSpace(protectedToken))
        {
            return false;
        }

        try
        {
            var json = _protector.Unprotect(protectedToken);
            var candidate = JsonSerializer.Deserialize<SessionAccessGrant>(json);
            if (candidate is null
                || candidate.SessionId != sessionId
                || candidate.ParticipantId == Guid.Empty
                || !Enum.IsDefined(candidate.Role))
            {
                return false;
            }

            grant = candidate;
            return true;
        }
        catch (Exception exception) when (
            exception is CryptographicException or JsonException)
        {
            return false;
        }
    }

    private static string GetCookieName(Guid sessionId)
    {
        return string.Concat(CookiePrefix, sessionId.ToString("N"));
    }
}

public sealed record SessionAccessGrant(
    Guid SessionId,
    Guid ParticipantId,
    SessionRole Role,
    DateTimeOffset ExpiresAtUtc);
