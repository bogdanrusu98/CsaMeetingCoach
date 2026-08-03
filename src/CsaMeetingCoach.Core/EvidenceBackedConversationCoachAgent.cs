using CsaMeetingCoach.Contracts;

namespace CsaMeetingCoach.Core;

public sealed class EvidenceBackedConversationCoachAgent(
    IConversationCoachAgent primaryAgent,
    IConversationCoachAgent deterministicEvaluator) : IConversationCoachAgent
{
    public async Task<CoachAgentDecision> AnalyzeAsync(
        CoachAgentContext context,
        TranscriptSegment latestSegment,
        CancellationToken cancellationToken)
    {
        var primaryDecision = await primaryAgent.AnalyzeAsync(
            context,
            latestSegment,
            cancellationToken);
        var deterministicDecision = await deterministicEvaluator.AnalyzeAsync(
            context,
            latestSegment,
            cancellationToken);
        var deterministicApprovals = deterministicDecision.ChecklistEvaluations
            .Where(evaluation => evaluation.ShouldComplete)
            .Select(evaluation => evaluation.ChecklistItemId)
            .ToHashSet();
        var safePrimaryEvaluations = primaryDecision.ChecklistEvaluations
            .Where(evaluation =>
                !evaluation.ShouldComplete
                || deterministicApprovals.Contains(evaluation.ChecklistItemId));

        return new CoachAgentDecision(
            safePrimaryEvaluations
                .Concat(deterministicDecision.ChecklistEvaluations)
                .ToArray(),
            primaryDecision.RecommendedTasks,
            (primaryDecision.RecommendationEvaluations ?? [])
                .Concat(deterministicDecision.RecommendationEvaluations ?? [])
                .ToArray())
        {
            ContextualCards = primaryDecision.ContextualCards
        };
    }
}
