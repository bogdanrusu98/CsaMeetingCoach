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
        var checklistItemId = Guid.NewGuid();
        var transcriptSegmentId = Guid.NewGuid();
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
              "checklist": [{
                "id": "{{checklistItemId}}",
                "title": "Confirm scope",
                "completionCriteria": "The scope is explicit.",
                "evidenceHints": ["scope"],
                "status": 0,
                "autoCompleted": false,
                "evidence": []
              }],
              "transcript": [{
                "id": "{{transcriptSegmentId}}",
                "speaker": "CSA",
                "text": "The earlier discussion mentioned scope.",
                "occurredAtUtc": "{{now.AddMinutes(-20):O}}",
                "isFinal": true
              }],
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
        Assert.Equal(
            1,
            Assert.Single(loaded.Checklist).CompletionEligibleFromTranscriptIndex);
        Assert.Equal(MeetingSessionState.CurrentSchemaVersion, loaded.StateSchemaVersion);
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

        var recommendation = Assert.Single(loaded!.RecommendedTasks);
        Assert.Equal(now, recommendation.AcceptedAtUtc);
        Assert.Equal(0, recommendation.CompletionEligibleFromTranscriptIndex);
        Assert.Empty(loaded.Checklist);
        Assert.Equal(MeetingSessionState.CurrentSchemaVersion, loaded.StateSchemaVersion);
    }

    [Fact]
    public async Task Get_SessionWithLegacyTranscriptSegmentMissingNewFields_LoadsSafely()
    {
        Directory.CreateDirectory(_directory);
        var sessionId = Guid.NewGuid();
        var segmentId = Guid.NewGuid();
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
              "revision": 1,
              "checklist": [],
              "transcript": [{
                "id": "{{segmentId}}",
                "speaker": "CSA",
                "text": "Azure Kubernetes Service is recommended.",
                "occurredAtUtc": "{{now.AddMinutes(-5):O}}",
                "isFinal": true
              }],
              "recommendedTasks": [],
              "warnings": []
            }
            """;
        await File.WriteAllTextAsync(
            Path.Combine(_directory, $"{sessionId:N}.json"),
            json);

        var loaded = await new JsonMeetingSessionStore(_directory).GetAsync(
            sessionId,
            CancellationToken.None);

        var segment = Assert.Single(loaded!.Transcript);
        Assert.Equal("Azure Kubernetes Service is recommended.", segment.Text);
        Assert.Null(segment.RecognizedText);
        Assert.Null(segment.CorrectionReason);
        Assert.Equal(MeetingSessionState.CurrentSchemaVersion, loaded.StateSchemaVersion);
    }

    [Fact]
    public async Task Get_VersionThreePendingChecklist_PreservesUnrestrictedEligibility()
    {
        var now = DateTimeOffset.UtcNow;
        var session = new MeetingSessionState(
            Guid.NewGuid(),
            TestData.CreatePurpose(),
            MeetingSessionStatus.Active,
            now,
            now,
            Revision: 1,
            Checklist:
            [
                new ChecklistItemState(
                    Guid.NewGuid(),
                    "Confirm scope",
                    "The scope is explicit.",
                    ["scope"],
                    ChecklistItemStatus.Pending,
                    AutoCompleted: false,
                    Confidence: null,
                    CompletionReason: null,
                    CompletedAtUtc: null,
                    Evidence: [],
                    CompletionEligibleFromTranscriptIndex: null)
            ],
            Transcript:
            [
                new TranscriptSegment(
                    Guid.NewGuid(),
                    "CSA",
                    "The scope is explicit.",
                    now,
                    IsFinal: true)
            ],
            RecommendedTasks: [],
            Warnings: [],
            StateSchemaVersion: 3);
        var store = new JsonMeetingSessionStore(_directory);
        await store.SaveAsync(session, CancellationToken.None);

        var loaded = await store.GetAsync(session.Id, CancellationToken.None);

        Assert.NotNull(loaded);
        Assert.Null(Assert.Single(loaded.Checklist).CompletionEligibleFromTranscriptIndex);
        Assert.Equal(MeetingSessionState.CurrentSchemaVersion, loaded.StateSchemaVersion);
    }

    [Fact]
    public async Task Get_VersionSixPresentation_InfersIntentAndConsolidatesParaphrases()
    {
        Directory.CreateDirectory(_directory);
        var sessionId = Guid.NewGuid();
        var firstRecommendationId = Guid.NewGuid();
        var secondRecommendationId = Guid.NewGuid();
        var firstSourceId = Guid.NewGuid();
        var secondSourceId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var json = $$"""
            {
              "id": "{{sessionId}}",
              "purpose": {
                "title": "Microsoft Entra ID presentation",
                "meetingType": "Presentation",
                "objective": "Explain identity and access capabilities",
                "successCriteria": []
              },
              "status": 0,
              "createdAtUtc": "{{now.AddMinutes(-10):O}}",
              "updatedAtUtc": "{{now:O}}",
              "revision": 2,
              "checklist": [],
              "transcript": [],
              "recommendedTasks": [{
                "id": "{{firstRecommendationId}}",
                "title": "Add an audience-relevant example illustrating lifecycle workflows",
                "rationale": "Connect the concepts to a practical example.",
                "confidence": 0.84,
                "sourceTranscriptSegmentIds": ["{{firstSourceId}}"],
                "status": 0,
                "createdAtUtc": "{{now.AddMinutes(-5):O}}"
              }, {
                "id": "{{secondRecommendationId}}",
                "title": "Add a concrete joiner-mover-leaver scenario with explicit access actions",
                "rationale": "Show access changes across the employee lifecycle.",
                "confidence": 0.96,
                "sourceTranscriptSegmentIds": ["{{secondSourceId}}"],
                "status": 0,
                "createdAtUtc": "{{now.AddMinutes(-4):O}}"
              }],
              "warnings": [],
              "template": 0,
              "stateSchemaVersion": 6
            }
            """;
        await File.WriteAllTextAsync(
            Path.Combine(_directory, $"{sessionId:N}.json"),
            json);

        var loaded = await new JsonMeetingSessionStore(_directory).GetAsync(
            sessionId,
            CancellationToken.None);

        Assert.NotNull(loaded);
        var recommendation = Assert.Single(loaded.RecommendedTasks);
        Assert.Equal(firstRecommendationId, recommendation.Id);
        Assert.Equal(
            "Add a concrete joiner-mover-leaver scenario with explicit access actions",
            recommendation.Title);
        Assert.Equal(0.96, recommendation.Confidence);
        Assert.Equal(
            RecommendationIntentPolicy.PresentationLifecycleExample,
            recommendation.IntentKey);
        Assert.Equal(
            [secondSourceId],
            recommendation.WordingSourceTranscriptSegmentIds);
        Assert.Equal(
            [secondSourceId, firstSourceId],
            recommendation.SourceTranscriptSegmentIds);
        Assert.Equal(MeetingSessionState.CurrentSchemaVersion, loaded.StateSchemaVersion);
    }

    [Fact]
    public async Task Get_VersionSixMixedTerminalStates_PreservesAcceptedRecommendation()
    {
        var now = DateTimeOffset.UtcNow;
        var dismissed = new RecommendedTaskState(
            Guid.NewGuid(),
            "Add an audience-relevant example illustrating lifecycle workflows",
            "Connect the concept to a practical example.",
            0.84,
            [Guid.NewGuid()],
            RecommendationStatus.Dismissed,
            now.AddMinutes(-5));
        var accepted = new RecommendedTaskState(
            Guid.NewGuid(),
            "Add a concrete joiner-mover-leaver scenario with explicit access actions",
            "Show access changes across the employee lifecycle.",
            0.96,
            [Guid.NewGuid()],
            RecommendationStatus.Accepted,
            now.AddMinutes(-4),
            AcceptedAtUtc: now.AddMinutes(-3));
        var session = new MeetingSessionState(
            Guid.NewGuid(),
            TestData.CreatePurpose(),
            MeetingSessionStatus.Active,
            now.AddMinutes(-10),
            now,
            Revision: 2,
            Checklist: [],
            Transcript: [],
            RecommendedTasks: [dismissed, accepted],
            Warnings: [],
            StateSchemaVersion: 6)
        {
            Template = SessionTemplateKind.Presentation,
            ExpiresAtUtc = now + SessionLifecycle.Lifetime
        };
        var store = new JsonMeetingSessionStore(_directory);
        await store.SaveAsync(session, CancellationToken.None);

        var loaded = await store.GetAsync(session.Id, CancellationToken.None);

        Assert.NotNull(loaded);
        Assert.Equal(2, loaded.RecommendedTasks.Count);
        var preserved = Assert.Single(loaded.RecommendedTasks.Where(
            item => item.Status == RecommendationStatus.Accepted));
        Assert.Equal(accepted.Id, preserved.Id);
        Assert.Equal(accepted.AcceptedAtUtc, preserved.AcceptedAtUtc);
        Assert.All(
            loaded.RecommendedTasks,
            item => Assert.Equal(
                RecommendationIntentPolicy.PresentationLifecycleExample,
                item.IntentKey));
        Assert.All(
            loaded.RecommendedTasks,
            item => Assert.NotEmpty(item.WordingSourceTranscriptSegmentIds));
        Assert.Equal(MeetingSessionState.CurrentSchemaVersion, loaded.StateSchemaVersion);
    }

    [Fact]
    public async Task ListAndDelete_RecoversCrashLeftTemporarySessionFile()
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
            ExpiresAtUtc = now + SessionLifecycle.Lifetime
        };
        await store.SaveAsync(session, CancellationToken.None);
        var canonicalPath = Path.Combine(_directory, $"{session.Id:N}.json");
        var temporaryPath = string.Concat(
            canonicalPath,
            ".",
            Guid.NewGuid().ToString("N"),
            ".tmp");
        File.Copy(canonicalPath, temporaryPath);
        File.Delete(canonicalPath);

        var listed = new List<MeetingSessionState>();
        await foreach (var item in store.ListAsync(CancellationToken.None))
        {
            listed.Add(item);
        }

        Assert.Equal(session.Id, Assert.Single(listed).Id);
        await store.DeleteAsync(session.Id, CancellationToken.None);
        Assert.False(File.Exists(temporaryPath));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
