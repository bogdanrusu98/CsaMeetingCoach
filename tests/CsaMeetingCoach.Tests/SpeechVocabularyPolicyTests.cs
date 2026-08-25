using CsaMeetingCoach.Contracts;
using CsaMeetingCoach.Core;

namespace CsaMeetingCoach.Tests;

public sealed class SpeechVocabularyPolicyTests
{
    [Theory]
    [InlineData(SessionTemplateKind.CsaVbd, "Culinary session", true)]
    [InlineData(SessionTemplateKind.Presentation, "Azure landing zone design", true)]
    [InlineData(SessionTemplateKind.Custom, "Culinary terminology and recipes", false)]
    public void UseAzureVocabulary_RespectsSessionDomain(
        SessionTemplateKind template,
        string objective,
        bool expected)
    {
        var now = DateTimeOffset.UtcNow;
        var session = new MeetingSessionState(
            Guid.NewGuid(),
            new MeetingPurpose("Domain test", "Custom", objective, []),
            MeetingSessionStatus.Active,
            now,
            now,
            1,
            [],
            [],
            [],
            [])
        {
            Template = template,
            ExpiresAtUtc = now + SessionLifecycle.Lifetime
        };

        Assert.Equal(expected, SpeechVocabularyPolicy.UseAzureVocabulary(session));
    }
}
