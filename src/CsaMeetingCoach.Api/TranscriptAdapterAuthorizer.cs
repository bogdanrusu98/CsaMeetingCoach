using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;

namespace CsaMeetingCoach.Api;

public enum TranscriptAdapterAuthMode
{
    Disabled,
    DevelopmentApiKey,
    Entra
}

public sealed record TranscriptAdapterAuthOptions(
    TranscriptAdapterAuthMode Mode,
    string DevelopmentApiKey,
    string RequiredRole);

public sealed class TranscriptAdapterUnavailableException(string message)
    : InvalidOperationException(message);

public sealed class TranscriptAdapterAuthorizer(TranscriptAdapterAuthOptions options)
{
    public const string ApiKeyHeaderName = "X-Transcript-Adapter-Key";

    public void Authorize(HttpContext context)
    {
        switch (options.Mode)
        {
            case TranscriptAdapterAuthMode.Disabled:
                throw new TranscriptAdapterUnavailableException(
                    "Transcript adapter ingestion is disabled.");
            case TranscriptAdapterAuthMode.DevelopmentApiKey:
                AuthorizeDevelopmentApiKey(context);
                return;
            case TranscriptAdapterAuthMode.Entra:
                AuthorizeEntraIdentity(context.User);
                return;
            default:
                throw new TranscriptAdapterUnavailableException(
                    "Transcript adapter authentication is not configured.");
        }
    }

    private void AuthorizeDevelopmentApiKey(HttpContext context)
    {
        if (!context.Request.Headers.TryGetValue(ApiKeyHeaderName, out var suppliedValues)
            || suppliedValues.Count != 1
            || string.IsNullOrWhiteSpace(suppliedValues[0]))
        {
            throw new UnauthorizedAccessException(
                "A valid transcript adapter credential is required.");
        }

        var suppliedHash = SHA256.HashData(
            Encoding.UTF8.GetBytes(suppliedValues[0]!));
        var expectedHash = SHA256.HashData(
            Encoding.UTF8.GetBytes(options.DevelopmentApiKey));
        if (!CryptographicOperations.FixedTimeEquals(suppliedHash, expectedHash))
        {
            throw new UnauthorizedAccessException(
                "A valid transcript adapter credential is required.");
        }
    }

    private void AuthorizeEntraIdentity(ClaimsPrincipal principal)
    {
        var hasRequiredRole = principal.Identity?.IsAuthenticated == true
            && principal.Claims.Any(claim =>
                claim.Type is "roles" or ClaimTypes.Role
                && string.Equals(
                    claim.Value,
                    options.RequiredRole,
                    StringComparison.Ordinal));

        if (!hasRequiredRole)
        {
            throw new UnauthorizedAccessException(
                $"The authenticated application requires the '{options.RequiredRole}' role.");
        }
    }
}
