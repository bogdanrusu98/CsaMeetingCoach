using CsaMeetingCoach.Contracts;

namespace CsaMeetingCoach.Core;

internal static class AlertRanker
{
    public static IReadOnlyList<RankedAlertCandidate> Rank(
        IReadOnlyList<AlertCandidate> candidates,
        IReadOnlyList<ContextualCardState> existingCards,
        IReadOnlySet<string> shownDefinitionKeys,
        IReadOnlySet<string> shownHintKeys)
    {
        var mostRecentCategories = existingCards
            .Reverse()
            .Select(TryResolveCategory)
            .Where(category => category is not null)
            .Take(3)
            .Cast<ConceptCategory>()
            .ToArray();

        return candidates
            .Select(candidate => new RankedAlertCandidate(
                candidate,
                Score(
                    candidate,
                    mostRecentCategories,
                    shownDefinitionKeys,
                    shownHintKeys)))
            .OrderByDescending(item => item.Score)
            .ThenByDescending(item => item.Candidate.LastSourceSegmentIndex)
            .ThenByDescending(item => item.Candidate.RequiresAzureVendorScope)
            .ThenBy(item => item.Candidate.ConceptKey, StringComparer.Ordinal)
            .ToArray();
    }

    private static double Score(
        AlertCandidate candidate,
        IReadOnlyList<ConceptCategory> mostRecentCategories,
        IReadOnlySet<string> shownDefinitionKeys,
        IReadOnlySet<string> shownHintKeys)
    {
        var score = (candidate.LastSourceSegmentIndex * 100)
            + candidate.Confidence * 10
            + (candidate.RequiresAzureVendorScope ? 18 : 8)
            + EducationalValue(candidate.Category)
            + Novelty(candidate, shownDefinitionKeys, shownHintKeys);

        if (mostRecentCategories.Contains(candidate.Category))
        {
            score -= 12;
        }

        return score;
    }

    private static double EducationalValue(ConceptCategory category) => category switch
    {
        ConceptCategory.IdentitySecurity => 16,
        ConceptCategory.DataAiIntegration => 15,
        ConceptCategory.ManagementGovernance => 14,
        ConceptCategory.Networking => 13,
        ConceptCategory.StorageDatabases => 12,
        ConceptCategory.ComputeContainersAppPlatforms => 12,
        ConceptCategory.MonitoringReliability => 11,
        ConceptCategory.MigrationDevOpsFinOps => 10,
        _ => 9
    };

    private static double Novelty(
        AlertCandidate candidate,
        IReadOnlySet<string> shownDefinitionKeys,
        IReadOnlySet<string> shownHintKeys)
    {
        return candidate.Kind switch
        {
            ContextualCardKind.Definition when !shownDefinitionKeys.Contains(candidate.ConceptKey) => 12,
            ContextualCardKind.Hint when !shownHintKeys.Contains(candidate.ConceptKey) => 10,
            ContextualCardKind.Hint => 4,
            _ => 0
        };
    }

    private static ConceptCategory? TryResolveCategory(ContextualCardState card)
    {
        if (!string.IsNullOrWhiteSpace(card.ConceptKey)
            && EducationalConceptCatalog.TryGetByConceptKey(card.ConceptKey, out var concept))
        {
            return concept.Category;
        }

        return EducationalConceptCatalog.TryResolveByAliasOrTitle(card.Title, out var aliasConcept)
            ? aliasConcept.Category
            : null;
    }
}

internal sealed record AlertCandidate(
    string ConceptKey,
    ContextualCardKind Kind,
    string Title,
    string Content,
    double Confidence,
    IReadOnlyList<Guid> SourceTranscriptSegmentIds,
    int LastSourceSegmentIndex,
    ConceptCategory Category,
    bool RequiresAzureVendorScope,
    string Source);

internal sealed record RankedAlertCandidate(AlertCandidate Candidate, double Score);
