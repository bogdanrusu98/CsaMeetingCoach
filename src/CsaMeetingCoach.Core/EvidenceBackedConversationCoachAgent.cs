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
        var deterministicEvaluations = new List<ChecklistEvaluation>();
        var deterministicRecommendationEvaluations =
            new List<RecommendationEvaluation>();
        foreach (var segment in TranscriptAnalysisWindow.Select(context.RecentTranscript))
        {
            var deterministicDecision = await deterministicEvaluator.AnalyzeAsync(
                context,
                segment,
                cancellationToken);
            deterministicEvaluations.AddRange(
                deterministicDecision.ChecklistEvaluations.Select(evaluation =>
                    evaluation.SourceTranscriptSegmentId is null
                        ? evaluation with { SourceTranscriptSegmentId = segment.Id }
                        : evaluation));
            deterministicRecommendationEvaluations.AddRange(
                (deterministicDecision.RecommendationEvaluations ?? [])
                    .Select(evaluation =>
                        evaluation.SourceTranscriptSegmentId is null
                            ? evaluation with { SourceTranscriptSegmentId = segment.Id }
                            : evaluation));
        }

        var deterministicApprovals = deterministicEvaluations
            .Where(evaluation => evaluation.ShouldComplete)
            .Select(evaluation => (
                evaluation.ChecklistItemId,
                evaluation.SourceTranscriptSegmentId))
            .ToHashSet();
        var safePrimaryEvaluations = primaryDecision.ChecklistEvaluations
            .Where(evaluation =>
                !evaluation.ShouldComplete
                || deterministicApprovals.Contains((
                    evaluation.ChecklistItemId,
                    evaluation.SourceTranscriptSegmentId ?? latestSegment.Id)));

        return new CoachAgentDecision(
            safePrimaryEvaluations
                .Concat(deterministicEvaluations)
                .ToArray(),
            primaryDecision.RecommendedTasks,
            (primaryDecision.RecommendationEvaluations ?? [])
                .Concat(deterministicRecommendationEvaluations)
                .ToArray())
        {
            ContextualCards = primaryDecision.ContextualCards
        };
    }
}
