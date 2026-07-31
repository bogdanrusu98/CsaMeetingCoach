using System.Security.Claims;
using CsaMeetingCoach.Api;
using Microsoft.AspNetCore.Http;

namespace CsaMeetingCoach.Tests;

public sealed class TranscriptAdapterAuthorizerTests
{
    [Fact]
    public void Authorize_WhenDisabled_RejectsIngestion()
    {
        var authorizer = CreateAuthorizer(TranscriptAdapterAuthMode.Disabled);

        Assert.Throws<TranscriptAdapterUnavailableException>(() =>
            authorizer.Authorize(new DefaultHttpContext()));
    }

    [Fact]
    public void Authorize_WithDevelopmentKey_RequiresExactCredential()
    {
        var authorizer = CreateAuthorizer(
            TranscriptAdapterAuthMode.DevelopmentApiKey);
        var invalidContext = new DefaultHttpContext();
        invalidContext.Request.Headers[
            TranscriptAdapterAuthorizer.ApiKeyHeaderName] = "wrong";

        Assert.Throws<UnauthorizedAccessException>(() =>
            authorizer.Authorize(invalidContext));

        var validContext = new DefaultHttpContext();
        validContext.Request.Headers[
            TranscriptAdapterAuthorizer.ApiKeyHeaderName] = DevelopmentKey;
        authorizer.Authorize(validContext);
    }

    [Fact]
    public void Authorize_WithEntraRole_RequiresAuthenticatedAppRole()
    {
        var authorizer = CreateAuthorizer(TranscriptAdapterAuthMode.Entra);
        var validContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim("roles", "TranscriptIngestor")],
                authenticationType: "Bearer"))
        };

        authorizer.Authorize(validContext);

        var missingRoleContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim("roles", "OtherRole")],
                authenticationType: "Bearer"))
        };
        Assert.Throws<UnauthorizedAccessException>(() =>
            authorizer.Authorize(missingRoleContext));
    }

    private const string DevelopmentKey =
        "local-development-key-with-32-characters";

    private static TranscriptAdapterAuthorizer CreateAuthorizer(
        TranscriptAdapterAuthMode mode)
    {
        return new TranscriptAdapterAuthorizer(new TranscriptAdapterAuthOptions(
            mode,
            DevelopmentKey,
            "TranscriptIngestor"));
    }
}
