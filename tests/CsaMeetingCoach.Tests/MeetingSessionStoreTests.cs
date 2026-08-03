using CsaMeetingCoach.Contracts;
using CsaMeetingCoach.Core;

namespace CsaMeetingCoach.Tests;

public sealed class MeetingSessionStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "CsaMeetingCoach.Tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task SaveAndGet_RoundTripsSession()
    {
        var store = new JsonMeetingSessionStore(_directory);
        var now = DateTimeOffset.UtcNow;
        var session = new MeetingSessionState(
            Guid.NewGuid(),
            TestData.CreatePurpose(),
            MeetingSessionStatus.Active,
            now,
            now,
            Revision: 1,
            Checklist: [],
            Transcript: [],
            RecommendedTasks: [],
            Warnings: [])
        {
            ContextualCards =
            [
                new ContextualCardState(
                    Guid.NewGuid(),
                    ContextualCardKind.Definition,
                    "RTO",
                    "Recovery Time Objective is the target restoration time.",
                    0.9,
                    [Guid.NewGuid()],
                    now)
            ]
        };

        await store.SaveAsync(session, CancellationToken.None);
        var loaded = await store.GetAsync(session.Id, CancellationToken.None);

        Assert.NotNull(loaded);
        Assert.Equal(session.Id, loaded.Id);
        Assert.Equal(session.Purpose.Objective, loaded.Purpose.Objective);
        var card = Assert.Single(loaded.ContextualCards);
        Assert.Equal(ContextualCardKind.Definition, card.Kind);
        Assert.Equal("RTO", card.Title);
    }

    [Fact]
    public async Task Get_OldJsonWithoutRecommendationLifecycleFields_UsesSafeDefaults()
    {
        Directory.CreateDirectory(_directory);
        var sessionId = Guid.NewGuid();
        var recommendationId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var json = $$"""
            {
              "id": "{{sessionId}}",
              "purpose": {
                "title": "Review",
                "meetingType": "Customer",
                "objective": "Review architecture",
                "successCriteria": []
              },
              "status": 0,
              "createdAtUtc": "{{now:O}}",
              "updatedAtUtc": "{{now:O}}",
              "revision": 1,
              "checklist": [],
              "transcript": [],
              "recommendedTasks": [{
                "id": "{{recommendationId}}",
                "title": "Discuss architecture",
                "rationale": "Help the customer.",
                "confidence": 0.8,
                "sourceTranscriptSegmentIds": [],
                "status": 2,
                "createdAtUtc": "{{now:O}}"
              }],
              "warnings": []
            }
            """;
        await File.WriteAllTextAsync(
            Path.Combine(_directory, $"{sessionId:N}.json"),
            json);

        var loaded = await new JsonMeetingSessionStore(_directory).GetAsync(
            sessionId,
            CancellationToken.None);

        var recommendation = Assert.Single(loaded!.RecommendedTasks);
        Assert.Null(recommendation.AcceptedAtUtc);
        Assert.Null(recommendation.CompletedAtUtc);
        Assert.Null(recommendation.CompletionReason);
        Assert.Empty(recommendation.Evidence!);
        Assert.Equal(RecommendationStatus.Dismissed, recommendation.Status);
        Assert.Empty(loaded.ContextualCards);
    }

    [Fact]
    public async Task Get_OldAcceptedRecommendation_UsesLoadTimeCutoff()
    {
        Directory.CreateDirectory(_directory);
        var sessionId = Guid.NewGuid();
        var recommendationId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var json = $$"""
            {
              "id": "{{sessionId}}",
              "purpose": {
                "title": "Review",
                "meetingType": "Customer",
                "objective": "Review architecture",
                "successCriteria": []
              },
              "status": 0,
              "createdAtUtc": "{{now.AddHours(-1):O}}",
              "updatedAtUtc": "{{now:O}}",
              "revision": 2,
              "checklist": [],
              "transcript": [],
              "recommendedTasks": [{
                "id": "{{recommendationId}}",
                "title": "Discuss architecture",
                "rationale": "Help the customer.",
                "confidence": 0.8,
                "sourceTranscriptSegmentIds": [],
                "status": 1,
                "createdAtUtc": "{{now.AddMinutes(-30):O}}"
              }],
              "warnings": []
            }
            """;
        await File.WriteAllTextAsync(
            Path.Combine(_directory, $"{sessionId:N}.json"),
            json);

        var loaded = await new JsonMeetingSessionStore(_directory).GetAsync(
            sessionId,
            CancellationToken.None);

        Assert.Equal(now, Assert.Single(loaded!.RecommendedTasks).AcceptedAtUtc);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
