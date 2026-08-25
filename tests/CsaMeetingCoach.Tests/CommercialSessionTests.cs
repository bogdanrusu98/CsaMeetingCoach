using CsaMeetingCoach.Api;
using CsaMeetingCoach.Contracts;
using CsaMeetingCoach.Core;
using Microsoft.Extensions.Logging.Abstractions;

namespace CsaMeetingCoach.Tests;

public sealed class CommercialSessionTests
{
    [Fact]
    public async Task CreatePresentation_CreatesHostAndImmutableTwentyFourHourExpiry()
    {
        var now = DateTimeOffset.UtcNow;
        var time = new MutableTimeProvider(now);
        using var coordinator = CreateCoordinator(time);

        var session = await coordinator.CreateAsync(
            new CreateMeetingSessionRequest(
                TestData.CreatePurpose(),
                Template: SessionTemplateKind.Presentation,
                HostDisplayName: "Ada"),
            CancellationToken.None);

        Assert.Equal(SessionTemplateKind.Presentation, session.Template);
        Assert.Equal(now + TimeSpan.FromHours(24), session.ExpiresAtUtc);
        var host = Assert.Single(session.Participants);
        Assert.Equal("Ada", host.DisplayName);
        Assert.Equal(SessionRole.Host, host.Role);
        Assert.Contains(
            session.Checklist,
            item => item.Title.Contains("audience", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task CreateSession_PersistsAudienceFamiliarityInHostAndMemberViews()
    {
        using var coordinator = CreateCoordinator(TimeProvider.System);

        var session = await coordinator.CreateAsync(
            new CreateMeetingSessionRequest(
                TestData.CreatePurpose(),
                AudienceFamiliarity: AudienceFamiliarity.Beginner),
            CancellationToken.None);

        Assert.Equal(AudienceFamiliarity.Beginner, session.AudienceFamiliarity);
        Assert.Equal(
            AudienceFamiliarity.Beginner,
            SessionViewProjector.ForMember(session).AudienceFamiliarity);
    }

    [Fact]
    public async Task ExpiredSession_RejectsNewTranscriptImmediately()
    {
        var now = DateTimeOffset.UtcNow;
        var time = new MutableTimeProvider(now);
        using var coordinator = CreateCoordinator(time);
        var session = await coordinator.CreateAsync(
            new CreateMeetingSessionRequest(TestData.CreatePurpose()),
            CancellationToken.None);
        time.Advance(SessionLifecycle.Lifetime);

        await Assert.ThrowsAsync<SessionExpiredException>(() =>
            coordinator.AddTranscriptAsync(
                session.Id,
                new AddTranscriptSegmentRequest("Host", "This must not be stored."),
                CancellationToken.None));
    }

    [Fact]
    public async Task JoinParticipant_AddsMemberWithoutChangingHost()
    {
        using var coordinator = CreateCoordinator(TimeProvider.System);
        var session = await coordinator.CreateAsync(
            new CreateMeetingSessionRequest(TestData.CreatePurpose()),
            CancellationToken.None);

        var (updated, member) = await coordinator.JoinParticipantAsync(
            session.Id,
            "Jordan",
            CancellationToken.None);

        Assert.Equal(SessionRole.Member, member.Role);
        Assert.Equal("Jordan", member.DisplayName);
        Assert.Equal(2, updated.Participants.Count);
        Assert.Single(updated.Participants, participant => participant.Role == SessionRole.Host);
    }

    [Fact]
    public async Task LeaveParticipant_RemovesOnlyTheActiveMember()
    {
        using var coordinator = CreateCoordinator(TimeProvider.System);
        var session = await coordinator.CreateAsync(
            new CreateMeetingSessionRequest(TestData.CreatePurpose()),
            CancellationToken.None);
        var (joined, member) = await coordinator.JoinParticipantAsync(
            session.Id,
            "Jordan",
            CancellationToken.None);

        var updated = await coordinator.LeaveParticipantAsync(
            joined.Id,
            member.Id,
            CancellationToken.None);

        Assert.Contains(
            updated.Participants,
            participant => participant.Id == member.Id
                && participant.Status == SessionParticipantStatus.Removed);
        Assert.Single(
            updated.Participants,
            participant => participant.Role == SessionRole.Host
                && participant.Status == SessionParticipantStatus.Active);
    }

    [Fact]
    public void MemberProjection_ExcludesPrivateStateAndUnpublishedAlerts()
    {
        var now = DateTimeOffset.UtcNow;
        var published = new ContextualCardState(
            Guid.NewGuid(),
            ContextualCardKind.Definition,
            "RTO",
            "Recovery Time Objective is the target restoration time.",
            0.9,
            [Guid.NewGuid()],
            now);
        var pending = published with
        {
            Id = Guid.NewGuid(),
            Title = "RPO",
            MemberAlertStatus = MemberAlertStatus.PendingApproval
        };
        var session = new MeetingSessionState(
            Guid.NewGuid(),
            TestData.CreatePurpose(),
            MeetingSessionStatus.Active,
            now,
            now,
            3,
            Checklist: [],
            Transcript:
            [
                new TranscriptSegment(
                    Guid.NewGuid(),
                    "Host",
                    "Private transcript",
                    now,
                    IsFinal: true)
            ],
            RecommendedTasks: [],
            Warnings: [])
        {
            ExpiresAtUtc = now + SessionLifecycle.Lifetime,
            ContextualCards = [published, pending],
            Participants =
            [
                new SessionParticipantState(
                    Guid.NewGuid(),
                    "Host",
                    SessionRole.Host,
                    SessionParticipantStatus.Active,
                    now)
            ],
            KnowledgeSources =
            [
                new KnowledgeSourceState(
                    Guid.NewGuid(),
                    KnowledgeSourceKind.File,
                    "private.txt",
                    KnowledgeSourceVisibility.HostPrivate,
                    KnowledgeSourceStatus.Ready,
                    now)
            ]
        };

        var view = SessionViewProjector.ForMember(session);

        var alert = Assert.Single(view.Alerts);
        Assert.Equal("RTO", alert.Title);
        Assert.DoesNotContain(view.Alerts, card => card.Title == "RPO");
        Assert.DoesNotContain(
            typeof(MemberSessionView).GetProperties(),
            property => property.Name is "Transcript" or "Participants"
                or "KnowledgeSources" or "RecommendedTasks");
        Assert.DoesNotContain(
            typeof(MemberAlertView).GetProperties(),
            property => property.Name is "SourceTranscriptSegmentIds"
                or "KnowledgeSourceIds" or "Confidence");
    }

    [Fact]
    public async Task JoinCodeStore_HashesResolvesAndDeletesCode()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "CsaMeetingCoach.JoinCodes",
            Guid.NewGuid().ToString("N"));
        try
        {
            var accessDirectory = Path.Combine(directory, "access");
            Directory.CreateDirectory(accessDirectory);
            var staleKeyPath = Path.Combine(
                accessDirectory,
                ".lookup-key.interrupted.tmp");
            await File.WriteAllBytesAsync(staleKeyPath, [1, 2, 3]);
            var store = new JsonSessionJoinCodeStore(directory);
            var sessionId = Guid.NewGuid();
            var code = await store.CreateAsync(
                sessionId,
                DateTimeOffset.UtcNow.AddHours(1),
                CancellationToken.None);

            Assert.Equal(sessionId, await store.ResolveAsync(
                code.ToLowerInvariant().Replace("-", " "),
                CancellationToken.None));
            var persisted = await File.ReadAllTextAsync(
                Path.Combine(directory, "access", $"{sessionId:N}.json"));
            Assert.DoesNotContain(
                code.Replace("-", ""),
                persisted,
                StringComparison.OrdinalIgnoreCase);
            Assert.False(File.Exists(staleKeyPath));
            Assert.Equal(
                32,
                new FileInfo(Path.Combine(accessDirectory, ".lookup-key")).Length);
            var recordPath = Path.Combine(
                accessDirectory,
                $"{sessionId:N}.json");
            var crashTemporaryPath = string.Concat(
                recordPath,
                ".",
                Guid.NewGuid().ToString("N"),
                ".tmp");
            File.Copy(recordPath, crashTemporaryPath);

            await store.DeleteAsync(sessionId, CancellationToken.None);
            Assert.Null(await store.ResolveAsync(code, CancellationToken.None));
            Assert.False(File.Exists(crashTemporaryPath));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task CleanupWorker_DeletesExpiredSessionCodeAndArtifacts()
    {
        var now = DateTimeOffset.UtcNow;
        var time = new MutableTimeProvider(now);
        var sessions = new InMemoryMeetingSessionStore();
        var joinCodes = new InMemorySessionJoinCodeStore();
        using var coordinator = new MeetingSessionCoordinator(
            sessions,
            new MeetingChecklistPlanner(),
            new HeuristicConversationCoachAgent(),
            aiAgent: null,
            new NullSessionUpdatePublisher(),
            timeProvider: time);
        var session = await coordinator.CreateAsync(
            new CreateMeetingSessionRequest(TestData.CreatePurpose()),
            CancellationToken.None);
        var code = await joinCodes.CreateAsync(
            session.Id,
            session.ExpiresAtUtc,
            CancellationToken.None);
        var cleaner = new RecordingArtifactCleaner();
        var broker = new SessionEventBroker();
        var worker = new SessionCleanupWorker(
            sessions,
            joinCodes,
            [cleaner],
            broker,
            coordinator,
            time,
            NullLogger<SessionCleanupWorker>.Instance);
        time.Advance(SessionLifecycle.Lifetime);

        await worker.DeleteExpiredSessionsAsync(CancellationToken.None);

        Assert.Null(await sessions.GetAsync(session.Id, CancellationToken.None));
        Assert.Null(await joinCodes.ResolveAsync(code, CancellationToken.None));
        Assert.Contains(session.Id, cleaner.DeletedSessionIds);
    }

    [Fact]
    public async Task AsyncAnalysis_UsesGenericTemplateAndAuthorizedKnowledge()
    {
        var sourceId = Guid.NewGuid();
        var knowledge = new StaticKnowledgeReader(
            new SessionKnowledgeSnippet(
                sourceId,
                "training.txt",
                KnowledgeSourceVisibility.MemberEligible,
                "A practice lab reinforces the learning objective."));
        var ai = new CapturingAgent();
        using var coordinator = new MeetingSessionCoordinator(
            new InMemoryMeetingSessionStore(),
            new MeetingChecklistPlanner(),
            new HeuristicConversationCoachAgent(),
            ai,
            new NullSessionUpdatePublisher(),
            analysisOptions: new AnalysisOptions(
                TimeSpan.Zero,
                TimeSpan.FromSeconds(5),
                TimeSpan.FromMilliseconds(1)),
            knowledgeReader: knowledge);
        var session = await coordinator.CreateAsync(
            new CreateMeetingSessionRequest(
                TestData.CreatePurpose(),
                Template: SessionTemplateKind.Training),
            CancellationToken.None);
        await coordinator.AddKnowledgeSourceAsync(
            session.Id,
            new KnowledgeSourceState(
                sourceId,
                KnowledgeSourceKind.File,
                "training.txt",
                KnowledgeSourceVisibility.MemberEligible,
                KnowledgeSourceStatus.Ready,
                DateTimeOffset.UtcNow,
                SizeBytes: 64),
            CancellationToken.None);

        await coordinator.AddTranscriptAsync(
            session.Id,
            new AddTranscriptSegmentRequest(
                "Trainer",
                "The practice lab reinforces the learning objective."),
            CancellationToken.None);
        var context = await ai.Context.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(SessionTemplateKind.Training, context.Template);
        Assert.Equal("training.txt", Assert.Single(context.Knowledge!).DisplayName);
    }

    [Fact]
    public async Task AsyncAnalysis_DoesNotCallProviderAfterExpiry()
    {
        var time = new MutableTimeProvider(DateTimeOffset.UtcNow);
        var ai = new CountingAgent();
        using var coordinator = new MeetingSessionCoordinator(
            new InMemoryMeetingSessionStore(),
            new MeetingChecklistPlanner(),
            new HeuristicConversationCoachAgent(),
            ai,
            new NullSessionUpdatePublisher(),
            analysisOptions: new AnalysisOptions(
                TimeSpan.FromMilliseconds(100),
                TimeSpan.FromSeconds(5),
                TimeSpan.FromMilliseconds(100)),
            timeProvider: time);
        var session = await coordinator.CreateAsync(
            new CreateMeetingSessionRequest(TestData.CreatePurpose()),
            CancellationToken.None);

        await coordinator.AddTranscriptAsync(
            session.Id,
            new AddTranscriptSegmentRequest("Host", "A final session statement."),
            CancellationToken.None);
        time.Advance(SessionLifecycle.Lifetime);
        await Task.Delay(250);

        Assert.Equal(0, ai.CallCount);
    }

    [Fact]
    public async Task RemovingKnowledge_CancelsAnalysisThatContainsIt()
    {
        var sourceId = Guid.NewGuid();
        var knowledge = new StaticKnowledgeReader(
            new SessionKnowledgeSnippet(
                sourceId,
                "private.txt",
                KnowledgeSourceVisibility.HostPrivate,
                "Private session guidance."));
        var ai = new CancellationObservingAgent();
        using var coordinator = new MeetingSessionCoordinator(
            new InMemoryMeetingSessionStore(),
            new MeetingChecklistPlanner(),
            new HeuristicConversationCoachAgent(),
            ai,
            new NullSessionUpdatePublisher(),
            analysisOptions: new AnalysisOptions(
                TimeSpan.Zero,
                TimeSpan.FromSeconds(10),
                TimeSpan.FromMilliseconds(1)),
            knowledgeReader: knowledge);
        var session = await coordinator.CreateAsync(
            new CreateMeetingSessionRequest(TestData.CreatePurpose()),
            CancellationToken.None);
        await coordinator.AddKnowledgeSourceAsync(
            session.Id,
            new KnowledgeSourceState(
                sourceId,
                KnowledgeSourceKind.File,
                "private.txt",
                KnowledgeSourceVisibility.HostPrivate,
                KnowledgeSourceStatus.Ready,
                DateTimeOffset.UtcNow,
                SizeBytes: 20),
            CancellationToken.None);
        await coordinator.AddTranscriptAsync(
            session.Id,
            new AddTranscriptSegmentRequest("Host", "Discuss the private guidance."),
            CancellationToken.None);
        await ai.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var updated = await coordinator.RemoveKnowledgeSourceAsync(
            session.Id,
            sourceId,
            CancellationToken.None);
        await ai.Cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Empty(updated.KnowledgeSources);
        Assert.Empty(updated.ContextualCards);
    }

    [Fact]
    public async Task RemovingKnowledge_RemovesDerivedMemberAndHostArtifacts()
    {
        var store = new InMemoryMeetingSessionStore();
        using var coordinator = new MeetingSessionCoordinator(
            store,
            new MeetingChecklistPlanner(),
            new HeuristicConversationCoachAgent(),
            aiAgent: null,
            new NullSessionUpdatePublisher());
        var session = await coordinator.CreateAsync(
            new CreateMeetingSessionRequest(TestData.CreatePurpose()),
            CancellationToken.None);
        var sourceId = Guid.NewGuid();
        session = await coordinator.AddKnowledgeSourceAsync(
            session.Id,
            new KnowledgeSourceState(
                sourceId,
                KnowledgeSourceKind.File,
                "private.txt",
                KnowledgeSourceVisibility.HostPrivate,
                KnowledgeSourceStatus.Ready,
                DateTimeOffset.UtcNow,
                SizeBytes: 20),
            CancellationToken.None);
        var segmentId = Guid.NewGuid();
        var card = new ContextualCardState(
            Guid.NewGuid(),
            ContextualCardKind.Definition,
            "Private term",
            "Private source-derived explanation.",
            0.9,
            [segmentId],
            DateTimeOffset.UtcNow)
        {
            MemberAlertStatus = MemberAlertStatus.Published,
            KnowledgeSourceIds = [sourceId]
        };
        var recommendation = new RecommendedTaskState(
            Guid.NewGuid(),
            "Use private guidance",
            "Derived from the private source.",
            0.9,
            [segmentId],
            RecommendationStatus.Proposed,
            DateTimeOffset.UtcNow)
        {
            KnowledgeSourceIds = [sourceId]
        };
        await store.SaveAsync(
            session with
            {
                ContextualCards = [card],
                RecommendedTasks = [recommendation]
            },
            CancellationToken.None);

        var updated = await coordinator.RemoveKnowledgeSourceAsync(
            session.Id,
            sourceId,
            CancellationToken.None);

        Assert.Empty(updated.KnowledgeSources);
        Assert.Empty(updated.ContextualCards);
        Assert.Empty(updated.RecommendedTasks);
        Assert.Empty(SessionViewProjector.ForMember(updated).Alerts);
    }

    [Fact]
    public async Task KnowledgeMutation_PersistsBeforeReportingNotificationFailure()
    {
        var store = new InMemoryMeetingSessionStore();
        using var creator = new MeetingSessionCoordinator(
            store,
            new MeetingChecklistPlanner(),
            new HeuristicConversationCoachAgent(),
            aiAgent: null,
            new NullSessionUpdatePublisher());
        var session = await creator.CreateAsync(
            new CreateMeetingSessionRequest(TestData.CreatePurpose()),
            CancellationToken.None);
        using var coordinator = new MeetingSessionCoordinator(
            store,
            new MeetingChecklistPlanner(),
            new HeuristicConversationCoachAgent(),
            aiAgent: null,
            new ThrowingPublisher());
        var source = new KnowledgeSourceState(
            Guid.NewGuid(),
            KnowledgeSourceKind.File,
            "persisted.txt",
            KnowledgeSourceVisibility.HostPrivate,
            KnowledgeSourceStatus.Ready,
            DateTimeOffset.UtcNow,
            SizeBytes: 10);

        var exception = await Assert.ThrowsAsync<SessionUpdateNotificationException>(() =>
            coordinator.AddKnowledgeSourceAsync(
                session.Id,
                source,
                CancellationToken.None));

        Assert.Contains(
            exception.PersistedSession.KnowledgeSources,
            item => item.Id == source.Id);
        var persisted = await store.GetAsync(session.Id, CancellationToken.None);
        Assert.Contains(persisted!.KnowledgeSources, item => item.Id == source.Id);
    }

    private static MeetingSessionCoordinator CreateCoordinator(TimeProvider timeProvider) =>
        new(
            new InMemoryMeetingSessionStore(),
            new MeetingChecklistPlanner(),
            new HeuristicConversationCoachAgent(),
            aiAgent: null,
            new NullSessionUpdatePublisher(),
            timeProvider: timeProvider);

    private sealed class MutableTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan duration) => _now += duration;
    }

    private sealed class RecordingArtifactCleaner : ISessionArtifactCleaner
    {
        public List<Guid> DeletedSessionIds { get; } = [];

        public Task DeleteSessionArtifactsAsync(
            Guid sessionId,
            CancellationToken cancellationToken)
        {
            DeletedSessionIds.Add(sessionId);
            return Task.CompletedTask;
        }
    }

    private sealed class StaticKnowledgeReader(SessionKnowledgeSnippet snippet)
        : ISessionKnowledgeReader
    {
        public Task<IReadOnlyList<SessionKnowledgeSnippet>> ReadAsync(
            Guid sessionId,
            IReadOnlyCollection<Guid> allowedSourceIds,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<SessionKnowledgeSnippet>>(
                allowedSourceIds.Contains(snippet.SourceId) ? [snippet] : []);
    }

    private sealed class CapturingAgent : IConversationCoachAgent
    {
        public TaskCompletionSource<CoachAgentContext> Context { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<CoachAgentDecision> AnalyzeAsync(
            CoachAgentContext context,
            TranscriptSegment latestSegment,
            CancellationToken cancellationToken)
        {
            Context.TrySetResult(context);
            return Task.FromResult(new CoachAgentDecision([], [], []));
        }
    }

    private sealed class CountingAgent : IConversationCoachAgent
    {
        private int _callCount;
        public int CallCount => Volatile.Read(ref _callCount);

        public Task<CoachAgentDecision> AnalyzeAsync(
            CoachAgentContext context,
            TranscriptSegment latestSegment,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _callCount);
            return Task.FromResult(new CoachAgentDecision([], [], []));
        }
    }

    private sealed class CancellationObservingAgent : IConversationCoachAgent
    {
        public TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Cancelled { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<CoachAgentDecision> AnalyzeAsync(
            CoachAgentContext context,
            TranscriptSegment latestSegment,
            CancellationToken cancellationToken)
        {
            Assert.Single(context.Knowledge!);
            Started.TrySetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                Cancelled.TrySetResult();
                throw;
            }

            return new CoachAgentDecision([], [], []);
        }
    }

    private sealed class ThrowingPublisher : ISessionUpdatePublisher
    {
        public Task PublishAsync(
            MeetingSessionState session,
            CancellationToken cancellationToken) =>
            Task.FromException(new IOException("Realtime delivery failed."));
    }
}
