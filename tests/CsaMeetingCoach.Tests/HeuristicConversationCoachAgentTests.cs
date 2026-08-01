using CsaMeetingCoach.Contracts;
using CsaMeetingCoach.Core;

namespace CsaMeetingCoach.Tests;

public sealed class HeuristicConversationCoachAgentTests
{
    [Fact]
    public async Task Analyze_AcceptedTalkingPoint_ReturnsCoverageEvaluation()
    {
        var latest = new TranscriptSegment(
            Guid.NewGuid(),
            "CSA",
            "We can reduce deployment risk by using staged rollout rings.",
            DateTimeOffset.UtcNow,
            IsFinal: true);
        var accepted = new RecommendedTaskState(
            Guid.NewGuid(),
            "Discuss deployment risk and staged rollout",
            "This helps the customer reduce deployment risk.",
            0.9,
            [Guid.NewGuid()],
            RecommendationStatus.Accepted,
            latest.OccurredAtUtc.AddMinutes(-2),
            latest.OccurredAtUtc.AddMinutes(-1),
            Evidence: []);
        var context = new CoachAgentContext(
            TestData.CreatePurpose(),
            [],
            [latest],
            [accepted]);

        var decision = await new HeuristicConversationCoachAgent().AnalyzeAsync(
            context,
            latest,
            CancellationToken.None);

        var evaluation = Assert.Single(decision.RecommendationEvaluations!);
        Assert.Equal(accepted.Id, evaluation.RecommendationId);
        Assert.True(evaluation.ShouldComplete);
        Assert.Equal(latest.Text, evaluation.EvidenceQuote);
    }
}
