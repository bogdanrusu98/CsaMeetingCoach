using System.Globalization;
using System.Text;
using CsaMeetingCoach.Contracts;

namespace CsaMeetingCoach.Core;

internal static class RecommendationIntentPolicy
{
    public const string PresentationOpeningOutcome = "presentation.opening-outcome";
    public const string PresentationClosingRecapActions = "presentation.closing-recap-actions";
    public const string PresentationLifecycleExample = "presentation.lifecycle-example";

    private const int MaximumMergedTranscriptSources = 20;

    public static string? Resolve(
        SessionTemplateKind template,
        string? title,
        string? rationale = null)
    {
        if (template != SessionTemplateKind.Presentation)
        {
            return null;
        }

        var normalized = Normalize($"{title} {rationale}");
        if (normalized.Length == 0)
        {
            return null;
        }

        if (IsLifecycleExample(normalized))
        {
            return PresentationLifecycleExample;
        }

        if (IsOpeningOutcome(normalized))
        {
            return PresentationOpeningOutcome;
        }

        return IsClosingRecap(normalized)
            ? PresentationClosingRecapActions
            : null;
    }

    public static string? Resolve(
        SessionTemplateKind template,
        RecommendedTaskState recommendation)
    {
        if (template != SessionTemplateKind.Presentation)
        {
            return null;
        }

        return IsKnown(recommendation.IntentKey)
            ? recommendation.IntentKey
            : Resolve(template, recommendation.Title, recommendation.Rationale);
    }

    public static IReadOnlySet<string> GetCoveredIntents(CoachAgentContext context)
    {
        return GetCoveredIntents(
            context.Template,
            context.Checklist,
            context.RecommendedTasks ?? []);
    }

    public static IReadOnlySet<string> GetCoveredIntents(
        SessionTemplateKind template,
        IReadOnlyCollection<ChecklistItemState> checklist,
        IReadOnlyCollection<RecommendedTaskState> recommendations)
    {
        var covered = new HashSet<string>(StringComparer.Ordinal);

        foreach (var item in checklist)
        {
            var intent = Resolve(template, item.Title, item.CompletionCriteria);
            if (intent is not null)
            {
                covered.Add(intent);
            }
        }

        foreach (var recommendation in recommendations)
        {
            var intent = Resolve(template, recommendation);
            if (intent is not null)
            {
                covered.Add(intent);
            }
        }

        return covered;
    }

    public static IReadOnlyList<RecommendedTaskState> Consolidate(
        SessionTemplateKind template,
        IReadOnlyCollection<ChecklistItemState> checklist,
        IReadOnlyList<RecommendedTaskState> recommendations)
    {
        if (template != SessionTemplateKind.Presentation || recommendations.Count == 0)
        {
            return recommendations;
        }

        var annotated = recommendations
            .Select(recommendation =>
            {
                var intent = Resolve(template, recommendation);
                return intent is null || string.Equals(
                        recommendation.IntentKey,
                        intent,
                        StringComparison.Ordinal)
                    ? recommendation
                    : recommendation with { IntentKey = intent };
            })
            .ToArray();
        var checklistIntents = checklist
            .Select(item => Resolve(template, item.Title, item.CompletionCriteria))
            .Where(intent => intent is not null)
            .Cast<string>()
            .ToHashSet(StringComparer.Ordinal);
        var removedIds = new HashSet<Guid>();
        var replacements = new Dictionary<Guid, RecommendedTaskState>();

        foreach (var group in annotated
                     .Where(recommendation => recommendation.IntentKey is not null)
                     .GroupBy(recommendation => recommendation.IntentKey!, StringComparer.Ordinal))
        {
            var items = group.ToArray();
            var proposed = items
                .Where(recommendation =>
                    recommendation.Status == RecommendationStatus.Proposed)
                .ToArray();
            var accepted = items
                .Where(recommendation =>
                    recommendation.Status == RecommendationStatus.Accepted)
                .ToArray();
            if (proposed.Length == 0 && accepted.Length == 0)
            {
                continue;
            }

            if (accepted.Length > 0)
            {
                var retained = Merge(accepted, group.Key);
                replacements[retained.Id] = retained;
                foreach (var recommendation in proposed.Concat(accepted))
                {
                    if (recommendation.Id != retained.Id)
                    {
                        removedIds.Add(recommendation.Id);
                    }
                }

                continue;
            }

            if (items.Any(recommendation => recommendation.Status is
                    RecommendationStatus.Completed or RecommendationStatus.Dismissed))
            {
                foreach (var recommendation in proposed)
                {
                    removedIds.Add(recommendation.Id);
                }

                continue;
            }

            if (checklistIntents.Contains(group.Key))
            {
                foreach (var recommendation in proposed)
                {
                    removedIds.Add(recommendation.Id);
                }

                continue;
            }

            var retainedProposal = Merge(proposed, group.Key);

            replacements[retainedProposal.Id] = retainedProposal;
            foreach (var recommendation in proposed)
            {
                if (recommendation.Id != retainedProposal.Id)
                {
                    removedIds.Add(recommendation.Id);
                }
            }
        }

        return annotated
            .Where(recommendation => !removedIds.Contains(recommendation.Id))
            .Select(recommendation => replacements.TryGetValue(
                recommendation.Id,
                out var replacement)
                ? replacement
                : recommendation)
            .ToArray();
    }

    public static RecommendedTaskState Merge(
        IReadOnlyCollection<RecommendedTaskState> recommendations,
        string intentKey)
    {
        if (recommendations.Count == 0)
        {
            throw new ArgumentException(
                "At least one recommendation is required.",
                nameof(recommendations));
        }

        var retained = recommendations
            .OrderBy(recommendation => recommendation.CreatedAtUtc)
            .ThenBy(recommendation => recommendation.Id)
            .First();
        var strongest = recommendations
            .OrderByDescending(recommendation => recommendation.Confidence)
            .ThenByDescending(recommendation => recommendation.CreatedAtUtc)
            .First();
        var accepted = recommendations
            .Where(recommendation => recommendation.Status == RecommendationStatus.Accepted)
            .ToArray();
        var strongestSources = GetWordingSources(strongest);
        var strongestSourceSet = strongestSources.ToHashSet();
        var mergedSources = strongestSources
            .Concat(recommendations
                .OrderByDescending(recommendation => recommendation.CreatedAtUtc)
                .ThenByDescending(recommendation => recommendation.Id)
                .SelectMany(recommendation =>
                    GetSupplementalSources(
                        recommendation,
                        recommendation.Id == strongest.Id))
                .Where(sourceId => !strongestSourceSet.Contains(sourceId))
                .Distinct()
                .Take(MaximumMergedTranscriptSources - strongestSources.Length))
            .ToArray();

        return retained with
        {
            Title = strongest.Title,
            Rationale = strongest.Rationale,
            Confidence = strongest.Confidence,
            IntentKey = intentKey,
            Status = accepted.Length > 0
                ? RecommendationStatus.Accepted
                : RecommendationStatus.Proposed,
            SourceTranscriptSegmentIds = mergedSources,
            WordingSourceTranscriptSegmentIds = strongestSources,
            KnowledgeSourceIds = recommendations
                .SelectMany(recommendation => recommendation.KnowledgeSourceIds)
                .Distinct()
                .ToArray(),
            AcceptedAtUtc = accepted
                .Select(recommendation => recommendation.AcceptedAtUtc)
                .Max(),
            CompletionEligibleFromTranscriptIndex = accepted
                .Select(recommendation => recommendation.CompletionEligibleFromTranscriptIndex)
                .Max()
        };
    }

    private static Guid[] GetWordingSources(RecommendedTaskState recommendation)
    {
        var availableSources = recommendation.SourceTranscriptSegmentIds.ToHashSet();
        var persistedSources = (recommendation.WordingSourceTranscriptSegmentIds ?? [])
            .Where(availableSources.Contains)
            .Distinct()
            .TakeLast(MaximumMergedTranscriptSources)
            .ToArray();
        return persistedSources.Length > 0
            ? persistedSources
            : recommendation.SourceTranscriptSegmentIds
                .Distinct()
                .TakeLast(MaximumMergedTranscriptSources)
                .ToArray();
    }

    private static IEnumerable<Guid> GetSupplementalSources(
        RecommendedTaskState recommendation,
        bool isStrongest)
    {
        var wordingSources = GetWordingSources(recommendation);
        var wordingSourceSet = wordingSources.ToHashSet();
        var supplemental = recommendation.SourceTranscriptSegmentIds
            .Where(sourceId => !wordingSourceSet.Contains(sourceId));
        return isStrongest
            ? supplemental
            : supplemental.Concat(wordingSources.Reverse());
    }

    internal static string Normalize(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var character in value.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) ==
                UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            builder.Append(char.IsLetterOrDigit(character)
                ? char.ToLowerInvariant(character)
                : ' ');
        }

        return string.Join(
            ' ',
            builder
                .ToString()
                .Normalize(NormalizationForm.FormC)
                .Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    private static bool IsKnown(string? intentKey)
    {
        return intentKey is PresentationOpeningOutcome
            or PresentationClosingRecapActions
            or PresentationLifecycleExample;
    }

    private static bool IsOpeningOutcome(string value)
    {
        var hasOpeningMarker = ContainsAny(
            value,
            "early",
            "at the start",
            "at the beginning",
            "from the start",
            "up front",
            "upfront",
            "outset",
            "opening",
            "introduction",
            "introductory",
            "frame the audience outcome",
            "de la inceput",
            "la inceput",
            "introducere");
        var hasOutcome = ContainsAny(
            value,
            "objective",
            "objectives",
            "purpose",
            "key message",
            "key messages",
            "audience outcome",
            "audience outcomes",
            "intended outcome",
            "intended outcomes",
            "presentation goal",
            "presentation goals",
            "obiectiv",
            "obiective",
            "scop",
            "mesaj cheie",
            "mesaje cheie",
            "rezultat pentru audienta");

        return hasOpeningMarker && hasOutcome;
    }

    private static bool IsClosingRecap(string value)
    {
        var hasClosingMarker = ContainsAny(
            value,
            "close the presentation",
            "close with the intended action",
            "closing summary",
            "closing recap",
            "at the conclusion",
            "presentation conclusion",
            "conclude the presentation",
            "concluding summary",
            "to conclude",
            "in conclusion",
            "wrap up",
            "final recap",
            "final summary",
            "incheierea prezentarii",
            "la finalul prezentarii",
            "concluzia prezentarii",
            "pentru a incheia");
        var hasRecap = ContainsAny(
            value,
            "summary",
            "summarize",
            "recap",
            "key message",
            "key messages",
            "takeaway",
            "takeaways",
            "next action",
            "next actions",
            "next step",
            "next steps",
            "intended action",
            "rezumat",
            "recapitulare",
            "mesaj cheie",
            "mesaje cheie",
            "pasii urmatori",
            "actiunea urmatoare");

        return hasClosingMarker && hasRecap;
    }

    private static bool IsLifecycleExample(string value)
    {
        var hasLifecycle = ContainsAny(
            value,
            "joiner mover leaver",
            "joiner",
            "mover",
            "leaver",
            "lifecycle workflow",
            "lifecycle workflows",
            "user lifecycle",
            "identity lifecycle",
            "access lifecycle",
            "onboarding and offboarding",
            "onboarding",
            "offboarding",
            "ciclul de viata",
            "angajare mutare plecare");
        var hasExample = ContainsAny(
            value,
            "example",
            "scenario",
            "illustrate",
            "illustrates",
            "illustrating",
            "illustration",
            "illustrative",
            "practical",
            "concrete",
            "walkthrough",
            "walk through",
            "story",
            "exemplu",
            "scenariu",
            "ilustra",
            "practic",
            "concret");

        return hasLifecycle && hasExample;
    }

    private static bool ContainsAny(string value, params string[] phrases)
    {
        var padded = $" {value} ";
        return phrases.Any(phrase => padded.Contains($" {phrase} ", StringComparison.Ordinal));
    }
}
