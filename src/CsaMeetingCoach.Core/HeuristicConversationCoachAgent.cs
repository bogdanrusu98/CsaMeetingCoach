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

    private static readonly string[] CustomerNeedCues =
    [
        "need",
        "require",
        "problem",
        "challenge",
        "how ",
        "what ",
        "can we",
        "could you",
        "trebuie",
        "nevoie",
        "problema",
        "provocare",
        "cum ",
        "ce ",
        "putem"
    ];

    private static readonly string[] ExplicitMeasurableOutcomeCues =
    [
        "success means",
        "success is measured",
        "success metric is",
        "success metric will",
        "metric is",
        "metric will",
        "measured by",
        "measure success",
        "kpi is",
        "kpi will",
        "target is",
        "target of",
        "success criteria are",
        "success criteria include",
        "success criteria will",
        "success criterion is",
        "succesul inseamna",
        "succesul este masurat",
        "succesul se masoara",
        "masuram succesul",
        "metrica este",
        "kpi este",
        "tinta este",
        "criteriile de succes sunt",
        "criteriul de succes este"
    ];

    private static readonly string[] NegatedMeasurableOutcomeCues =
    [
        "no success criteria",
        "without success criteria",
        "success criteria are not",
        "success criteria have not",
        "success criteria remain undefined",
        "success criteria remain unclear",
        "metric is not",
        "metric has not",
        "metric remains undefined",
        "target is not",
        "target has not",
        "target remains undefined",
        "target remains unclear",
        "nu exista criterii de succes",
        "fara criterii de succes",
        "criteriile de succes nu",
        "metrica nu este",
        "tinta nu este"
    ];

    public Task<CoachAgentDecision> AnalyzeAsync(
        CoachAgentContext context,
        TranscriptSegment latestSegment,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!latestSegment.IsFinal || string.IsNullOrWhiteSpace(latestSegment.Text))
        {
            return Task.FromResult(new CoachAgentDecision([], [], []));
        }

        var normalizedText = Normalize(latestSegment.Text);
        var evaluations = context.Checklist
            .Where(item => item.Status == ChecklistItemStatus.Pending)
            .Select(item => Evaluate(item, latestSegment, normalizedText))
            .Where(evaluation => evaluation is not null)
            .Cast<ChecklistEvaluation>()
            .ToArray();

        var recommendations = CreateRecommendations(latestSegment, normalizedText);
        var recommendationEvaluations = (context.RecommendedTasks ?? [])
            .Where(item => item.Status == RecommendationStatus.Accepted)
            .Select(item => EvaluateRecommendation(item, latestSegment, normalizedText))
            .Where(evaluation => evaluation is not null)
            .Cast<RecommendationEvaluation>()
            .ToArray();
        return Task.FromResult(new CoachAgentDecision(
            evaluations,
            recommendations,
            recommendationEvaluations));
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
        var requiresMeasurableOutcome = RequiresMeasurableOutcome(item);
        var requiresCommitment = normalizedTitle.Contains("next step", StringComparison.Ordinal)
            || normalizedTitle.Contains("owner", StringComparison.Ordinal)
            || normalizedTitle.Contains("urmator", StringComparison.Ordinal);

        var hasQuantitativeOutcome =
            QuantitativeOutcomeRegex().IsMatch(segment.Text);
        var hasExplicitMeasurableOutcome =
            ExplicitMeasurableOutcomeCues.Any(normalizedText.Contains)
            && !NegatedMeasurableOutcomeCues.Any(normalizedText.Contains);
        var hasMeasurableOutcomeEvidence =
            hasQuantitativeOutcome || hasExplicitMeasurableOutcome;

        if (requiresMeasurableOutcome && !hasMeasurableOutcomeEvidence)
        {
            confidence = Math.Min(confidence, 0.70);
        }

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

    internal static bool RequiresMeasurableOutcome(ChecklistItemState item)
    {
        var definition = Normalize($"{item.Title} {item.CompletionCriteria}");
        return definition.Contains("success criteria", StringComparison.Ordinal)
            || definition.Contains("success criterion", StringComparison.Ordinal)
            || definition.Contains("success metric", StringComparison.Ordinal)
            || definition.Contains("measurable outcome", StringComparison.Ordinal)
            || definition.Contains("criteriu de succes", StringComparison.Ordinal)
            || definition.Contains("rezultat masurabil", StringComparison.Ordinal);
    }

    private static IReadOnlyList<RecommendedTaskProposal> CreateRecommendations(
        TranscriptSegment segment,
        string normalizedText)
    {
        if (!CommitmentCues.Any(normalizedText.Contains)
            && !CustomerNeedCues.Any(normalizedText.Contains)
            && !segment.Text.Contains('?'))
        {
            return [];
        }

        var topic = WhitespaceRegex().Replace(segment.Text.Trim(), " ");
        if (topic.Length > 125)
        {
            topic = string.Concat(topic.AsSpan(0, 122), "...");
        }

        return
        [
            new(
                $"Discuss next: {topic}",
                "Addressing this explicit need, question, or next step helps the customer get a clear, useful outcome during the meeting.",
                0.86,
                [segment.Id])
        ];
    }

    private static RecommendationEvaluation? EvaluateRecommendation(
        RecommendedTaskState recommendation,
        TranscriptSegment segment,
        string normalizedText)
    {
        var titleTerms = Normalize(recommendation.Title)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(term => term.Length >= 5 && term is not "discuss")
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var matches = titleTerms.Count(term =>
            normalizedText.Contains(term, StringComparison.Ordinal));
        var requiredMatches = Math.Min(2, titleTerms.Length);
        if (matches < requiredMatches || requiredMatches == 0)
        {
            return null;
        }

        var confidence = Math.Min(0.95, 0.82 + ((matches - 1) * 0.03));
        return new RecommendationEvaluation(
            recommendation.Id,
            ShouldComplete: true,
            confidence,
            "The latest final segment explicitly discusses the accepted talking point.",
            segment.Text.Trim());
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

    [GeneratedRegex(
        @"(?:\d+(?:[.,]\d+)?\s*(?:%|\b(?:percent(?:age)?|procente?|milliseconds?|milisecunde?|seconds?|secunde?|minutes?|minute|hours?|ore|days?|zile|weeks?|saptamani|months?|luni)\b)|\b(?:availability|disponibilitate|reduction|reducere|increase|crestere|decrease|scadere)\b(?:\s+\p{L}+){0,3}\s+\d+(?:[.,]\d+)?\s*%?)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex QuantitativeOutcomeRegex();
}
