using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using CsaMeetingCoach.Contracts;

namespace CsaMeetingCoach.Core;

public sealed partial class HeuristicConversationCoachAgent : IConversationCoachAgent
{
    private static readonly string[] CommitmentCues =
    [
        "we will",
        "i will",
        "you will",
        "need to",
        "should",
        "follow up",
        "send",
        "prepare",
        "schedule",
        "vom",
        "voi",
        "veți",
        "trebuie să",
        "ar trebui să",
        "trimite",
        "pregăti",
        "programa"
    ];

    public Task<CoachAgentDecision> AnalyzeAsync(
        CoachAgentContext context,
        TranscriptSegment latestSegment,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!latestSegment.IsFinal || string.IsNullOrWhiteSpace(latestSegment.Text))
        {
            return Task.FromResult(new CoachAgentDecision([], []));
        }

        var normalizedText = Normalize(latestSegment.Text);
        var evaluations = context.Checklist
            .Where(item => item.Status == ChecklistItemStatus.Pending)
            .Select(item => Evaluate(item, latestSegment, normalizedText))
            .Where(evaluation => evaluation is not null)
            .Cast<ChecklistEvaluation>()
            .ToArray();

        var recommendations = CreateRecommendations(latestSegment, normalizedText);
        return Task.FromResult(new CoachAgentDecision(evaluations, recommendations));
    }

    private static ChecklistEvaluation? Evaluate(
        ChecklistItemState item,
        TranscriptSegment segment,
        string normalizedText)
    {
        var matchedHints = item.EvidenceHints
            .Select(Normalize)
            .Where(hint => hint.Length >= 3 && normalizedText.Contains(hint, StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (matchedHints.Length == 0)
        {
            return null;
        }

        var confidence = Math.Min(0.97, 0.84 + ((matchedHints.Length - 1) * 0.03));
        var normalizedTitle = Normalize(item.Title);
        var requiresCommitment = normalizedTitle.Contains("next step", StringComparison.Ordinal)
            || normalizedTitle.Contains("owner", StringComparison.Ordinal)
            || normalizedTitle.Contains("urmator", StringComparison.Ordinal);

        if (requiresCommitment && !CommitmentCues.Any(normalizedText.Contains))
        {
            confidence = Math.Min(confidence, 0.70);
        }

        return new ChecklistEvaluation(
            item.Id,
            ShouldComplete: confidence >= MeetingSessionCoordinator.AutoCompletionThreshold,
            confidence,
            $"Matched discussion evidence: {string.Join(", ", matchedHints)}.",
            segment.Text.Trim());
    }

    private static IReadOnlyList<RecommendedTaskProposal> CreateRecommendations(
        TranscriptSegment segment,
        string normalizedText)
    {
        if (!CommitmentCues.Any(normalizedText.Contains))
        {
            return [];
        }

        var title = WhitespaceRegex().Replace(segment.Text.Trim(), " ");
        if (title.Length > 140)
        {
            title = string.Concat(title.AsSpan(0, 137), "...");
        }

        return
        [
            new(
                title,
                "The discussion contains an explicit commitment or follow-up cue.",
                0.86,
                [segment.Id])
        ];
    }

    internal static string Normalize(string value)
    {
        var decomposed = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);

        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(char.ToLowerInvariant(character));
            }
        }

        return WhitespaceRegex()
            .Replace(NonWordRegex().Replace(builder.ToString(), " "), " ")
            .Trim();
    }

    [GeneratedRegex(@"[^\p{L}\p{N}]+", RegexOptions.CultureInvariant)]
    private static partial Regex NonWordRegex();

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex WhitespaceRegex();
}
