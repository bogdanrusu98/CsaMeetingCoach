using System.Text.RegularExpressions;
using CsaMeetingCoach.Contracts;

namespace CsaMeetingCoach.Core;

public static class SpeechVocabularyPolicy
{
    private static readonly string[] AzureTerms =
    [
        "azure",
        "microsoft entra",
        "cloud adoption",
        "cloud architecture",
        "cloud migration",
        "aks",
        "rbac",
        "landing zone",
        "resource group",
        "availability zone"
    ];

    public static bool UseAzureVocabulary(MeetingSessionState session)
    {
        if (session.Template == SessionTemplateKind.CsaVbd)
        {
            return true;
        }

        var purpose = string.Join(
            ' ',
            session.Purpose.Title,
            session.Purpose.MeetingType,
            session.Purpose.Objective,
            string.Join(' ', session.Purpose.SuccessCriteria));
        return AzureTerms.Any(term => Regex.IsMatch(
            purpose,
            $@"(?<![\p{{L}}\p{{N}}]){Regex.Escape(term)}(?![\p{{L}}\p{{N}}])",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
            TimeSpan.FromMilliseconds(100)));
    }
}
