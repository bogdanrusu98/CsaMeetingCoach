using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Channels;
using CsaMeetingCoach.Contracts;
using Microsoft.Extensions.Logging;

namespace CsaMeetingCoach.Core;

public sealed class MeetingSessionCoordinator : IDisposable
{
    public const double AutoCompletionThreshold = 0.82;
    private const double ContextualCardThreshold = 0.75;
    private const int MaximumContextualCardsPerAnalysis = 1;
    private const int MaximumRetainedContextualCards = 12;
    private const string AnalysisUnavailableWarning =
        "AI coaching is temporarily unavailable. Transcript and local checklist processing continued.";
    private static readonly TimeSpan DefinitionToHintCooldown = TimeSpan.FromMinutes(3);
    private static readonly TimeSpan HintRepeatCooldown = TimeSpan.FromMinutes(15);
    private static readonly string[] SalesOrRecommendationPatterns =
    [
        "recommend",
        "should consider",
        "you should",
        "next step",
        "action item",
        "reach out",
        "follow up",
        "ask the client",
        "ask the customer",
        "sales motion"
    ];

    private readonly IMeetingSessionStore _store;
    private readonly IMeetingChecklistPlanner _checklistPlanner;
    private readonly IConversationCoachAgent _deterministicAgent;
    private readonly IConversationCoachAgent? _aiAgent;
    private readonly ISessionUpdatePublisher _updatePublisher;
    private readonly ILogger<MeetingSessionCoordinator>? _logger;
    private readonly AnalysisOptions _analysisOptions;
    private readonly TimeProvider _timeProvider;
    private readonly ISessionKnowledgeReader? _knowledgeReader;
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _sessionLocks = new();
    private readonly ConcurrentDictionary<Guid, SessionAnalysisState> _analysisStates = new();
    private readonly CancellationTokenSource _disposalCts = new();

    public MeetingSessionCoordinator(
        IMeetingSessionStore store,
        IMeetingChecklistPlanner checklistPlanner,
        IConversationCoachAgent deterministicAgent,
        IConversationCoachAgent? aiAgent,
        ISessionUpdatePublisher updatePublisher,
        ILogger<MeetingSessionCoordinator>? logger = null,
        AnalysisOptions? analysisOptions = null,
        TimeProvider? timeProvider = null,
        ISessionKnowledgeReader? knowledgeReader = null)
    {
        _store = store;
        _checklistPlanner = checklistPlanner;
        _deterministicAgent = deterministicAgent;
        _aiAgent = aiAgent;
        _updatePublisher = updatePublisher;
        _logger = logger;
        _analysisOptions = analysisOptions ?? AnalysisOptions.Default;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _knowledgeReader = knowledgeReader;
        _analysisOptions.Validate();
    }


    public async Task<MeetingSessionState> CreateAsync(
        CreateMeetingSessionRequest request,
        CancellationToken cancellationToken)
    {
        ValidatePurpose(request.Purpose);
        ValidateTemplate(request.Template);
        ValidateMemberAlertMode(request.MemberAlertMode);
        ValidateAudienceFamiliarity(request.AudienceFamiliarity);
        var now = _timeProvider.GetUtcNow();
        var host = new SessionParticipantState(
            Guid.NewGuid(),
            NormalizeParticipantName(request.HostDisplayName, "Host"),
            SessionRole.Host,
            SessionParticipantStatus.Active,
            now);
        var session = new MeetingSessionState(
            Guid.NewGuid(),
            request.Purpose,
            MeetingSessionStatus.Active,
            now,
            now,
            Revision: 1,
            _checklistPlanner.CreateChecklist(
                request.Purpose,
                request.Checklist,
                request.Template),
            Transcript: [],
            RecommendedTasks: [],
            Warnings: [],
            TeamsOnlineMeetingId: NormalizeMeetingId(request.TeamsOnlineMeetingId),
            StateSchemaVersion: MeetingSessionState.CurrentSchemaVersion)
        {
            Template = request.Template,
            MemberAlertMode = request.MemberAlertMode,
            AudienceFamiliarity = request.AudienceFamiliarity,
            ExpiresAtUtc = now + SessionLifecycle.Lifetime,
            Participants = [host]
        };

        await _store.SaveAsync(session, cancellationToken);
        await _updatePublisher.PublishAsync(session, cancellationToken);
        return session;
    }

    public async Task<MeetingSessionState?> GetAsync(
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        var session = await _store.GetAsync(sessionId, cancellationToken);
        return session is null ? null : ApplyRuntimeAnalysisState(session);
    }

    public async Task<(MeetingSessionState Session, SessionParticipantState Participant)>
        JoinParticipantAsync(
            Guid sessionId,
            string displayName,
            CancellationToken cancellationToken)
    {
        SessionParticipantState? participant = null;
        var updated = await MutateAsync(
            sessionId,
            session =>
            {
                EnsureActive(session);
                if (session.Participants.Count(participantState =>
                        participantState.Status == SessionParticipantStatus.Active) >= 100)
                {
                    throw new InvalidOperationException(
                        "This session has reached its participant limit.");
                }

                participant = new SessionParticipantState(
                    Guid.NewGuid(),
                    NormalizeParticipantName(displayName, "Member"),
                    SessionRole.Member,
                    SessionParticipantStatus.Active,
                    _timeProvider.GetUtcNow());
                return Task.FromResult(session with
                {
                    Participants = session.Participants.Append(participant).ToArray()
                });
            },
            cancellationToken);
        return (updated, participant!);
    }

    public Task<MeetingSessionState> LeaveParticipantAsync(
        Guid sessionId,
        Guid participantId,
        CancellationToken cancellationToken) =>
        MutateAsync(
            sessionId,
            session =>
            {
                var participant = session.Participants.SingleOrDefault(candidate =>
                    candidate.Id == participantId
                    && candidate.Role == SessionRole.Member
                    && candidate.Status == SessionParticipantStatus.Active);
                if (participant is null)
                {
                    throw new UnauthorizedAccessException(
                        "Only an active session member can leave this view.");
                }

                return Task.FromResult(session with
                {
                    Participants = session.Participants
                        .Select(candidate => candidate.Id == participantId
                            ? candidate with { Status = SessionParticipantStatus.Removed }
                            : candidate)
                        .ToArray()
                });
            },
            cancellationToken);

    public async Task<MeetingSessionState> AddTranscriptAsync(
        Guid sessionId,
        AddTranscriptSegmentRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Speaker))
        {
            throw new ArgumentException("Speaker is required.", nameof(request));
        }

        if (string.IsNullOrWhiteSpace(request.Text))
        {
            throw new ArgumentException("Transcript text is required.", nameof(request));
        }

        var gate = _sessionLocks.GetOrAdd(sessionId, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var session = await _store.GetAsync(sessionId, cancellationToken)
                ?? throw new KeyNotFoundException($"Meeting session {sessionId} was not found.");
            EnsureActive(session);

            if (request.SourceSegmentId == Guid.Empty)
            {
                throw new ArgumentException(
                    "Source segment ID cannot be an empty GUID.",
                    nameof(request));
            }

            var existingSegment = request.SourceSegmentId is { } sourceSegmentId
                ? session.Transcript.FirstOrDefault(
                    item => item.SourceSegmentId == sourceSegmentId)
                : null;

            TranscriptSegment segment;
            IReadOnlyList<TranscriptSegment> transcript;
            MeetingSessionState transcriptUpdate;
            if (existingSegment is not null)
            {
                if (!existingSegment.IsFinal || existingSegment.AnalyzedAtUtc is not null)
                {
                    return session;
                }

                // Final segment in transcript but fast lane did not yet complete (prior failure).
                // Re-run the fast lane below.
                segment = existingSegment;
                transcript = session.Transcript;
                transcriptUpdate = session;
            }
            else
            {
                var trimmedText = request.Text.Trim();
                var normalizeResult = SpeechNormalizer.Normalize(
                    trimmedText,
                    request.IsSpeechRecognized && request.IsFinal,
                    session.Purpose);

                segment = new TranscriptSegment(
                    Guid.NewGuid(),
                    request.Speaker.Trim(),
                    normalizeResult.NormalizedText,
                    request.OccurredAtUtc ?? _timeProvider.GetUtcNow(),
                    request.IsFinal,
                    request.SourceSegmentId)
                {
                    RecognizedText = normalizeResult.WasCorrected ? trimmedText : null,
                    CorrectionReason = normalizeResult.CorrectionReason
                };

                if (normalizeResult.WasCorrected)
                {
                    _logger?.LogInformation(
                        "Speech normalization applied to segment preparation. SessionId: {SessionId}, SegmentId: {SegmentId}, CorrectionReason: {CorrectionReason}",
                        sessionId,
                        segment.Id,
                        normalizeResult.CorrectionReason);
                }

                transcript = session.Transcript.Append(segment).ToArray();
                transcriptUpdate = session with
                {
                    Transcript = transcript,
                    Revision = session.Revision + 1,
                    UpdatedAtUtc = _timeProvider.GetUtcNow()
                };
                await _store.SaveAsync(transcriptUpdate, cancellationToken);
                await _updatePublisher.PublishAsync(transcriptUpdate, cancellationToken);
            }

            if (!segment.IsFinal)
            {
                return transcriptUpdate;
            }

            // Fast synchronous lane: deterministic checklist + CrossCuttingPolicy.
            var finalTranscript = transcript
                .Where(transcriptSegment => transcriptSegment.IsFinal)
                .ToArray();
            var analysisWindow = TranscriptAnalysisWindow.Select(finalTranscript);
            var knowledge = _knowledgeReader is null
                ? []
                : await _knowledgeReader.ReadAsync(
                    sessionId,
                    transcriptUpdate.KnowledgeSources
                        .Where(source => source.Status == KnowledgeSourceStatus.Ready)
                        .Select(source => source.Id)
                        .ToArray(),
                    cancellationToken);
            var context = new CoachAgentContext(
                transcriptUpdate.Purpose,
                transcriptUpdate.Checklist,
                finalTranscript,
                transcriptUpdate.RecommendedTasks,
                FilterClientReadyCards(transcriptUpdate.ContextualCards),
                transcriptUpdate.Template,
                knowledge,
                transcriptUpdate.AudienceFamiliarity);
            var deterministicDecision = await _deterministicAgent.AnalyzeAsync(
                context, segment, cancellationToken);
            var fastLaneDecision = (_aiAgent is null
                ? deterministicDecision
                : deterministicDecision with { RecommendedTasks = [] }) with
            {
                ContextualCards = PresentationCoachingPolicy.SelectContextualCards(
                    context,
                    deterministicDecision.ContextualCards,
                    analysisWindow)
            };
            var decision = CrossCuttingRecommendationPolicy.Apply(
                fastLaneDecision,
                segment,
                transcriptUpdate.Template);

            var warnings = transcriptUpdate.Warnings
                .Where(warning => string.Equals(
                    warning,
                    AnalysisUnavailableWarning,
                    StringComparison.Ordinal))
                .ToList();
            var checklist = ApplyChecklistEvaluations(
                transcriptUpdate.Checklist,
                decision.ChecklistEvaluations,
                analysisWindow,
                segment,
                finalTranscript,
                transcriptUpdate.Template,
                warnings);
            var evaluatedRecommendations = ApplyRecommendationEvaluations(
                transcriptUpdate.RecommendedTasks,
                decision.RecommendationEvaluations ?? [],
                analysisWindow,
                segment,
                finalTranscript,
                warnings);
            var recommendations = ApplyRecommendations(
                evaluatedRecommendations,
                transcriptUpdate.Template,
                decision.RecommendedTasks,
                finalTranscript,
                transcriptUpdate.Checklist,
                transcriptUpdate.Purpose,
                transcriptUpdate.KnowledgeSources
                    .Where(source => source.Status == KnowledgeSourceStatus.Ready)
                    .Select(source => source.Id)
                    .ToArray(),
                warnings);
            var contextualCardResult = ApplyContextualCards(
                transcriptUpdate,
                decision.ContextualCards,
                analysisWindow,
                knowledge);
            var analyzedAtUtc = DateTimeOffset.UtcNow;
            var analyzedTranscript = transcript
                .Select(item => item.Id == segment.Id
                    ? item with { AnalyzedAtUtc = analyzedAtUtc }
                    : item)
                .ToArray();

            var fastLaneUpdate = transcriptUpdate with
            {
                Transcript = analyzedTranscript,
                Checklist = checklist,
                RecommendedTasks = recommendations,
                ContextualCards = contextualCardResult.Cards,
                ShownDefinitionKeys = contextualCardResult.ShownDefinitionKeys,
                ShownHintKeys = contextualCardResult.ShownHintKeys,
                DefinitionCooldowns = contextualCardResult.DefinitionCooldowns,
                HintCooldowns = contextualCardResult.HintCooldowns,
                ContentFingerprints = contextualCardResult.ContentFingerprints,
                LastEducationalCardSegmentIndex =
                    contextualCardResult.LastEducationalCardSegmentIndex,
                Warnings = NormalizeWarnings(warnings),
                IsAnalyzing = _aiAgent is not null,
                Revision = transcriptUpdate.Revision + 1,
                UpdatedAtUtc = analyzedAtUtc
            };

            await _store.SaveAsync(fastLaneUpdate, cancellationToken);
            await _updatePublisher.PublishAsync(fastLaneUpdate, cancellationToken);

            if (_aiAgent is not null)
            {
                SignalAsyncAnalysisLane(sessionId);
            }

            return fastLaneUpdate;
        }
        finally
        {
            gate.Release();
        }
    }

    private void SignalAsyncAnalysisLane(Guid sessionId)
    {
        var state = _analysisStates.GetOrAdd(sessionId, static _ => new SessionAnalysisState());
        state.EnsureBackgroundTask(() => Task.Run(async () =>
        {
            using var linkedCancellation = CancellationTokenSource
                .CreateLinkedTokenSource(
                    _disposalCts.Token,
                    state.CancellationToken);
            await RunAnalysisLoopAsync(
                sessionId,
                state,
                linkedCancellation.Token);
        }));

        state.Signal();
    }

    private async Task<MeetingSessionState> MutateKnowledgeAsync(
        Guid sessionId,
        Func<MeetingSessionState, MeetingSessionState> mutation,
        CancellationToken cancellationToken)
    {
        var gate = _sessionLocks.GetOrAdd(sessionId, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var session = await _store.GetAsync(sessionId, cancellationToken)
                ?? throw new KeyNotFoundException($"Meeting session {sessionId} was not found.");
            EnsureActive(session);
            var mutated = mutation(session);
            var updated = mutated with
            {
                IsAnalyzing = false,
                Revision = session.Revision + 1,
                UpdatedAtUtc = _timeProvider.GetUtcNow()
            };

            await _store.SaveAsync(updated, cancellationToken);
            try
            {
                CancelSessionAnalysis(sessionId);
                ResignalAnalysisAfterKnowledgeChange(updated);
                await _updatePublisher.PublishAsync(updated, cancellationToken);
            }
            catch (Exception exception)
            {
                throw new SessionUpdateNotificationException(updated, exception);
            }

            return updated;
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task RunAnalysisLoopAsync(
        Guid sessionId,
        SessionAnalysisState state,
        CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var trigger in state.TriggerReader.ReadAllAsync(cancellationToken))
            {
                var analysisGeneration = trigger;
                var batchStartedAt = DateTimeOffset.UtcNow;
                var quietDeadline = batchStartedAt + _analysisOptions.DebounceWindow;
                var maximumDeadline =
                    batchStartedAt + _analysisOptions.EffectiveMaximumBatchWindow;
                while (true)
                {
                    var deadline = quietDeadline <= maximumDeadline
                        ? quietDeadline
                        : maximumDeadline;
                    var remaining = deadline - DateTimeOffset.UtcNow;
                    if (remaining <= TimeSpan.Zero)
                    {
                        break;
                    }

                    using var delayCts = CancellationTokenSource
                        .CreateLinkedTokenSource(cancellationToken);
                    delayCts.CancelAfter(remaining);
                    try
                    {
                        var hasMore = await state.TriggerReader.WaitToReadAsync(delayCts.Token);
                        if (hasMore)
                        {
                            while (state.TriggerReader.TryRead(out var nextGeneration))
                            {
                                analysisGeneration = Math.Max(
                                    analysisGeneration,
                                    nextGeneration);
                            }
                            quietDeadline =
                                DateTimeOffset.UtcNow + _analysisOptions.DebounceWindow;
                        }
                    }
                    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                    {
                        break; // debounce deadline expired
                    }
                }

                try
                {
                    await RunAiAnalysisAsync(
                        sessionId,
                        state,
                        analysisGeneration,
                        cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    state.MarkCompleted(analysisGeneration);
                    _logger?.LogError(
                        ex,
                        "Async AI coaching analysis failed for session {SessionId}.",
                        sessionId);
                    if (state.LatestRequestedGeneration <= analysisGeneration)
                    {
                        await TryAddAnalysisWarningAsync(
                            sessionId,
                            AnalysisUnavailableWarning,
                            state,
                            analysisGeneration);
                    }
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Normal shutdown.
        }
        catch (Exception ex)
        {
            _logger?.LogError(
                ex,
                "Analysis loop unexpectedly terminated for session {SessionId}.",
                sessionId);
        }
    }

    private async Task RunAiAnalysisAsync(
        Guid sessionId,
        SessionAnalysisState state,
        long analysisGeneration,
        CancellationToken cancellationToken)
    {
        // Load current session to build context for AI.
        MeetingSessionState session;
        TranscriptSegment latestSegment;
        IReadOnlyList<TranscriptSegment> analysisWindow;
        CoachAgentContext context;
        IReadOnlyDictionary<Guid, KnowledgeSourceVisibility> authorizedKnowledge;
        {
            var loadGate = _sessionLocks.GetOrAdd(sessionId, static _ => new SemaphoreSlim(1, 1));
            await loadGate.WaitAsync(cancellationToken);
            try
            {
                var loaded = await _store.GetAsync(sessionId, cancellationToken);
                if (loaded is null)
                {
                    state.MarkCompleted(analysisGeneration);
                    return;
                }

                if (loaded.Status != MeetingSessionStatus.Active
                    || SessionLifecycle.IsExpired(loaded, _timeProvider.GetUtcNow()))
                {
                    state.MarkCompleted(analysisGeneration);
                    if (loaded.Status != MeetingSessionStatus.Active)
                    {
                        await ClearAnalyzingFlagAsync(loaded, cancellationToken);
                    }
                    return;
                }

                var finalTranscript = loaded.Transcript
                    .Where(s => s.IsFinal)
                    .ToArray();
                if (finalTranscript.Length == 0)
                {
                    state.MarkCompleted(analysisGeneration);
                    await ClearAnalyzingFlagAsync(loaded, cancellationToken);
                    return;
                }

                session = loaded;
                latestSegment = finalTranscript[finalTranscript.Length - 1];
                analysisWindow = TranscriptAnalysisWindow.Select(finalTranscript);
                authorizedKnowledge = session.KnowledgeSources
                    .Where(source => source.Status == KnowledgeSourceStatus.Ready)
                    .ToDictionary(source => source.Id, source => source.Visibility);
                var knowledge = _knowledgeReader is null
                    ? []
                    : await _knowledgeReader.ReadAsync(
                        sessionId,
                        authorizedKnowledge.Keys.ToArray(),
                        cancellationToken);
                context = new CoachAgentContext(
                    session.Purpose,
                    session.Checklist,
                    finalTranscript,
                    session.RecommendedTasks,
                    FilterClientReadyCards(session.ContextualCards),
                    session.Template,
                    knowledge,
                    session.AudienceFamiliarity);
            }
            finally
            {
                loadGate.Release();
            }
        }

        // Call AI with a bounded timeout.
        var remainingLifetime = session.ExpiresAtUtc - _timeProvider.GetUtcNow();
        if (remainingLifetime <= TimeSpan.Zero)
        {
            state.MarkCompleted(analysisGeneration);
            return;
        }

        CoachAgentDecision aiDecision;
        using var aiCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        aiCts.CancelAfter(
            remainingLifetime < _analysisOptions.AiTimeout
                ? remainingLifetime
                : _analysisOptions.AiTimeout);
        try
        {
            aiDecision = await _aiAgent!.AnalyzeAsync(context, latestSegment, aiCts.Token);
        }
        catch (OperationCanceledException)
            when (SessionLifecycle.IsExpired(session, _timeProvider.GetUtcNow()))
        {
            state.MarkCompleted(analysisGeneration);
            return;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            state.MarkCompleted(analysisGeneration);
            _logger?.LogWarning(
                "AI coaching analysis timed out for session {SessionId}.",
                sessionId);
            if (state.LatestRequestedGeneration <= analysisGeneration)
            {
                await TryAddAnalysisWarningAsync(
                    sessionId,
                    AnalysisUnavailableWarning,
                    state,
                    analysisGeneration);
            }
            return;
        }

        // Merge AI decision into the current session state under the gate.
        var mergeGate = _sessionLocks.GetOrAdd(sessionId, static _ => new SemaphoreSlim(1, 1));
        await mergeGate.WaitAsync(cancellationToken);
        try
        {
            var current = await _store.GetAsync(sessionId, cancellationToken);
            if (current is null)
            {
                state.MarkCompleted(analysisGeneration);
                return;
            }

            if (current.Status != MeetingSessionStatus.Active
                || SessionLifecycle.IsExpired(current, _timeProvider.GetUtcNow()))
            {
                state.MarkCompleted(analysisGeneration);
                if (current.Status != MeetingSessionStatus.Active)
                {
                    await ClearAnalyzingFlagAsync(current, cancellationToken);
                }
                return;
            }
            if (!HasSameKnowledgeAuthorization(current, authorizedKnowledge))
            {
                state.MarkCompleted(analysisGeneration);
                return;
            }

            var finalTranscript = current.Transcript
                .Where(s => s.IsFinal)
                .ToArray();

            var mergeWarnings = current.Warnings
                .Where(warning => !string.Equals(
                    warning,
                    AnalysisUnavailableWarning,
                    StringComparison.Ordinal))
                .ToList();

            // A completed snapshot remains grounded even when newer speech is queued.
            // Evidence is checked against the segment that the agent actually analyzed.
            var checklist = ApplyChecklistEvaluations(
                current.Checklist,
                aiDecision.ChecklistEvaluations,
                analysisWindow,
                latestSegment,
                finalTranscript,
                current.Template,
                mergeWarnings);
            var evaluatedRecommendations = ApplyRecommendationEvaluations(
                current.RecommendedTasks,
                aiDecision.RecommendationEvaluations ?? [],
                analysisWindow,
                latestSegment,
                finalTranscript,
                mergeWarnings);
            var recommendations = ApplyRecommendations(
                evaluatedRecommendations,
                current.Template,
                aiDecision.RecommendedTasks,
                finalTranscript,
                current.Checklist,
                current.Purpose,
                current.KnowledgeSources
                    .Where(source => source.Status == KnowledgeSourceStatus.Ready)
                    .Select(source => source.Id)
                    .ToArray(),
                mergeWarnings);
            var contextualCardResult = ApplyContextualCards(
                current,
                aiDecision.ContextualCards,
                analysisWindow,
                context.Knowledge ?? []);

            state.MarkCompleted(analysisGeneration);
            var mergedSession = current with
            {
                Checklist = checklist,
                RecommendedTasks = recommendations,
                ContextualCards = contextualCardResult.Cards,
                ShownDefinitionKeys = contextualCardResult.ShownDefinitionKeys,
                ShownHintKeys = contextualCardResult.ShownHintKeys,
                DefinitionCooldowns = contextualCardResult.DefinitionCooldowns,
                HintCooldowns = contextualCardResult.HintCooldowns,
                ContentFingerprints = contextualCardResult.ContentFingerprints,
                LastEducationalCardSegmentIndex =
                    contextualCardResult.LastEducationalCardSegmentIndex,
                Warnings = NormalizeWarnings(mergeWarnings),
                IsAnalyzing = state.IsAnalyzing,
                Revision = current.Revision + 1,
                UpdatedAtUtc = DateTimeOffset.UtcNow
            };

            await _store.SaveAsync(mergedSession, cancellationToken);
            await _updatePublisher.PublishAsync(mergedSession, cancellationToken);
        }
        finally
        {
            mergeGate.Release();
        }
    }

    private async Task ClearAnalyzingFlagAsync(
        MeetingSessionState session,
        CancellationToken cancellationToken)
    {
        if (SessionLifecycle.IsExpired(session, _timeProvider.GetUtcNow()))
        {
            return;
        }

        var cleared = session with
        {
            IsAnalyzing = false,
            Revision = session.Revision + 1,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        };
        await _store.SaveAsync(cleared, cancellationToken);
        await _updatePublisher.PublishAsync(cleared, cancellationToken);
    }

    private async Task TryAddAnalysisWarningAsync(
        Guid sessionId,
        string warning,
        SessionAnalysisState state,
        long analysisGeneration)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try
        {
            var warningGate = _sessionLocks.GetOrAdd(sessionId, static _ => new SemaphoreSlim(1, 1));
            await warningGate.WaitAsync(cts.Token);
            try
            {
                var session = await _store.GetAsync(sessionId, cts.Token);
                if (session is null)
                {
                    return;
                }
                if (SessionLifecycle.IsExpired(session, _timeProvider.GetUtcNow()))
                {
                    return;
                }
                if (state.LatestRequestedGeneration > analysisGeneration)
                {
                    return;
                }

                var updated = session with
                {
                    Warnings = NormalizeWarnings(session.Warnings.Append(warning)),
                    IsAnalyzing = state.IsAnalyzing,
                    Revision = session.Revision + 1,
                    UpdatedAtUtc = DateTimeOffset.UtcNow
                };
                await _store.SaveAsync(updated, cts.Token);
                await _updatePublisher.PublishAsync(updated, cts.Token);
            }
            finally
            {
                warningGate.Release();
            }
        }
        catch (OperationCanceledException ex) when (cts.IsCancellationRequested)
        {
            _logger?.LogWarning(
                ex,
                "Timed out while persisting an AI analysis warning for session {SessionId}.",
                sessionId);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger?.LogError(
                ex,
                "Failed to persist analysis warning for session {SessionId}.",
                sessionId);
        }
    }

    public void Dispose()
    {
        _disposalCts.Cancel();
        _disposalCts.Dispose();
        foreach (var state in _analysisStates.Values)
        {
            state.Dispose();
        }
    }

    public void CancelSessionAnalysis(Guid sessionId)
    {
        if (_analysisStates.TryRemove(sessionId, out var state))
        {
            state.Cancel();
            state.Dispose();
        }
    }

    public Task<MeetingSessionState> ReopenChecklistItemAsync(
        Guid sessionId,
        Guid checklistItemId,
        CancellationToken cancellationToken)
    {
        return MutateAsync(sessionId, session =>
        {
            var found = false;
            var checklist = session.Checklist.Select(item =>
            {
                if (item.Id != checklistItemId)
                {
                    return item;
                }

                found = true;
                return item with
                {
                    Status = ChecklistItemStatus.Pending,
                    AutoCompleted = false,
                    Confidence = null,
                    CompletionReason = null,
                    CompletedAtUtc = null,
                    Evidence = [],
                    CompletionEligibleFromTranscriptIndex =
                        session.Transcript.Count(segment => segment.IsFinal)
                };
            }).ToArray();

            if (!found)
            {
                throw new KeyNotFoundException($"Checklist item {checklistItemId} was not found.");
            }

            return Task.FromResult(session with { Checklist = checklist });
        }, cancellationToken);
    }

    public Task<MeetingSessionState> SetRecommendationStatusAsync(
        Guid sessionId,
        Guid recommendationId,
        RecommendationStatus status,
        CancellationToken cancellationToken)
    {
        if (status != RecommendationStatus.Accepted
            && status != RecommendationStatus.Dismissed)
        {
            throw new ArgumentException("A recommendation can only be accepted or dismissed.", nameof(status));
        }

        return MutateAsync(sessionId, session =>
        {
            var found = false;
            var recommendations = session.RecommendedTasks.Select(task =>
            {
                if (task.Id != recommendationId)
                {
                    return task;
                }

                found = true;
                if (task.Status != RecommendationStatus.Proposed)
                {
                    throw new InvalidOperationException(
                        $"Recommended task {recommendationId} is no longer proposed.");
                }

                return task with
                {
                    Status = status,
                    AcceptedAtUtc = status == RecommendationStatus.Accepted
                        ? DateTimeOffset.UtcNow
                        : null,
                    CompletionEligibleFromTranscriptIndex =
                        status == RecommendationStatus.Accepted
                            ? session.Transcript.Count(segment => segment.IsFinal)
                            : null
                };
            }).ToArray();

            if (!found)
            {
                throw new KeyNotFoundException($"Recommended task {recommendationId} was not found.");
            }

            return Task.FromResult(session with { RecommendedTasks = recommendations });
        }, cancellationToken);
    }

    public Task<MeetingSessionState> ReopenRecommendationAsync(
        Guid sessionId,
        Guid recommendationId,
        CancellationToken cancellationToken)
    {
        return MutateAsync(sessionId, session =>
        {
            var found = false;
            var recommendations = session.RecommendedTasks.Select(task =>
            {
                if (task.Id != recommendationId)
                {
                    return task;
                }

                found = true;
                if (task.Status != RecommendationStatus.Completed)
                {
                    throw new InvalidOperationException(
                        $"Recommended task {recommendationId} is not completed.");
                }

                return task with
                {
                    Status = RecommendationStatus.Accepted,
                    CompletedAtUtc = null,
                    CompletionReason = null,
                    Evidence = [],
                    CompletionEligibleFromTranscriptIndex =
                        session.Transcript.Count(segment => segment.IsFinal)
                };
            }).ToArray();

            if (!found)
            {
                throw new KeyNotFoundException($"Recommended task {recommendationId} was not found.");
            }

            return Task.FromResult(session with { RecommendedTasks = recommendations });
        }, cancellationToken);
    }

    public Task<MeetingSessionState> CompleteAsync(
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        return MutateAsync(
            sessionId,
            session => Task.FromResult(session with { Status = MeetingSessionStatus.Completed }),
            cancellationToken);
    }

    public async Task<MeetingSessionState> AddKnowledgeSourceAsync(
        Guid sessionId,
        KnowledgeSourceState source,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.Id == Guid.Empty
            || string.IsNullOrWhiteSpace(source.DisplayName)
            || !Enum.IsDefined(source.Kind)
            || !Enum.IsDefined(source.Visibility)
            || !Enum.IsDefined(source.Status)
            || source.SizeBytes < 0)
        {
            throw new ArgumentException("Knowledge source metadata is invalid.", nameof(source));
        }

        return await MutateKnowledgeAsync(
            sessionId,
            session =>
            {
                if (session.KnowledgeSources.Any(existing => existing.Id == source.Id))
                {
                    throw new InvalidOperationException(
                        $"Knowledge source {source.Id} already exists.");
                }

                if (session.KnowledgeSources.Count >= SessionKnowledgeLimits.MaximumSourceCount)
                {
                    throw new InvalidOperationException(
                        $"A session can contain at most {SessionKnowledgeLimits.MaximumSourceCount} knowledge sources.");
                }

                var totalBytes = checked(
                    session.KnowledgeSources.Sum(existing => existing.SizeBytes)
                    + source.SizeBytes);
                if (totalBytes > SessionKnowledgeLimits.MaximumTotalBytes)
                {
                    throw new InvalidOperationException(
                        $"Session knowledge exceeds the {SessionKnowledgeLimits.MaximumTotalMegabytes} MB aggregate limit.");
                }

                return session with
                {
                    KnowledgeSources = session.KnowledgeSources.Append(source).ToArray()
                };
            },
            cancellationToken);
    }

    public async Task<MeetingSessionState> RemoveKnowledgeSourceAsync(
        Guid sessionId,
        Guid sourceId,
        CancellationToken cancellationToken)
    {
        return await MutateKnowledgeAsync(
            sessionId,
            session =>
            {
                if (!session.KnowledgeSources.Any(source => source.Id == sourceId))
                {
                    throw new KeyNotFoundException(
                        $"Knowledge source {sourceId} was not found.");
                }

                return session with
                {
                    KnowledgeSources = session.KnowledgeSources
                        .Where(source => source.Id != sourceId)
                        .ToArray(),
                    ContextualCards = session.ContextualCards
                        .Where(card => !(card.KnowledgeSourceIds ?? []).Contains(sourceId))
                        .ToArray(),
                    RecommendedTasks = session.RecommendedTasks
                        .Where(task => !(task.KnowledgeSourceIds ?? []).Contains(sourceId))
                        .ToArray()
                };
            },
            cancellationToken);
    }

    private void ResignalAnalysisAfterKnowledgeChange(MeetingSessionState session)
    {
        if (_aiAgent is not null
            && session.Status == MeetingSessionStatus.Active
            && !SessionLifecycle.IsExpired(session, _timeProvider.GetUtcNow())
            && session.Transcript.Any(segment => segment.IsFinal))
        {
            SignalAsyncAnalysisLane(session.Id);
        }
    }

    public Task<MeetingSessionState> SetMemberAlertStatusAsync(
        Guid sessionId,
        Guid cardId,
        MemberAlertStatus status,
        CancellationToken cancellationToken)
    {
        if (status is not MemberAlertStatus.Published and not MemberAlertStatus.Hidden)
        {
            throw new ArgumentException(
                "A member alert can only be published or hidden.",
                nameof(status));
        }

        return MutateAsync(
            sessionId,
            session =>
            {
                EnsureActive(session);
                var found = false;
                var cards = session.ContextualCards.Select(card =>
                {
                    if (card.Id != cardId)
                    {
                        return card;
                    }

                    found = true;
                    return card with { MemberAlertStatus = status };
                }).ToArray();
                if (!found)
                {
                    throw new KeyNotFoundException(
                        $"Contextual card {cardId} was not found.");
                }

                return Task.FromResult(session with { ContextualCards = cards });
            },
            cancellationToken);
    }

    private async Task<MeetingSessionState> MutateAsync(
        Guid sessionId,
        Func<MeetingSessionState, Task<MeetingSessionState>> mutation,
        CancellationToken cancellationToken)
    {
        var gate = _sessionLocks.GetOrAdd(sessionId, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var session = await _store.GetAsync(sessionId, cancellationToken)
                ?? throw new KeyNotFoundException($"Meeting session {sessionId} was not found.");
            EnsureNotExpired(session);
            var updated = await mutation(session);
            updated = updated with
            {
                IsAnalyzing = updated.Status == MeetingSessionStatus.Active
                    && IsAnalysisPending(sessionId),
                Revision = session.Revision + 1,
                UpdatedAtUtc = _timeProvider.GetUtcNow()
            };

            await _store.SaveAsync(updated, cancellationToken);
            await _updatePublisher.PublishAsync(updated, cancellationToken);
            return updated;
        }
        finally
        {
            gate.Release();
        }
    }

    private static IReadOnlyList<ChecklistItemState> ApplyChecklistEvaluations(
        IReadOnlyList<ChecklistItemState> current,
        IReadOnlyList<ChecklistEvaluation> evaluations,
        IReadOnlyList<TranscriptSegment> analysisWindow,
        TranscriptSegment latestSegment,
        IReadOnlyList<TranscriptSegment> finalTranscript,
        SessionTemplateKind template,
        ICollection<string> warnings)
    {
        var knownIds = current.Select(item => item.Id).ToHashSet();
        var currentById = current.ToDictionary(item => item.Id);
        var transcriptIndexById = finalTranscript
            .Select((segment, index) => (segment.Id, Index: index))
            .ToDictionary(item => item.Id, item => item.Index);
        foreach (var evaluation in evaluations)
        {
            if (evaluation is null)
            {
                warnings.Add("Rejected an empty checklist evaluation.");
                continue;
            }

            if (!knownIds.Contains(evaluation.ChecklistItemId))
            {
                warnings.Add(
                    $"Rejected checklist completion for unknown item {evaluation.ChecklistItemId}.");
                continue;
            }

            if (evaluation.ShouldComplete
                && (!double.IsFinite(evaluation.Confidence)
                    || evaluation.Confidence is < 0 or > 1
                    || string.IsNullOrWhiteSpace(evaluation.Reason)
                    || string.IsNullOrWhiteSpace(evaluation.EvidenceQuote)))
            {
                warnings.Add(
                    $"Rejected completion for checklist item {evaluation.ChecklistItemId}: evaluation data was invalid.");
            }
            else if (evaluation.ShouldComplete
                && ResolveEvidenceSegment(
                    evaluation.SourceTranscriptSegmentId,
                    evaluation.EvidenceQuote,
                    analysisWindow,
                    latestSegment) is null)
            {
                warnings.Add(
                    $"Rejected checklist completion for item {evaluation.ChecklistItemId}: agent evidence was not present in its cited analysis-window segment.");
            }
        }

        var byItem = evaluations
            .Select(evaluation => (
                Evaluation: evaluation,
                EvidenceSegment: evaluation is null
                    ? null
                    : ResolveEvidenceSegment(
                        evaluation.SourceTranscriptSegmentId,
                        evaluation.EvidenceQuote,
                        analysisWindow,
                        latestSegment)))
            .Where(candidate => candidate.Evaluation is not null
                && currentById.TryGetValue(
                    candidate.Evaluation.ChecklistItemId,
                    out var checklistItem)
                && candidate.Evaluation.ShouldComplete
                && double.IsFinite(candidate.Evaluation.Confidence)
                && candidate.Evaluation.Confidence is >= AutoCompletionThreshold and <= 1
                && !string.IsNullOrWhiteSpace(candidate.Evaluation.Reason)
                && candidate.EvidenceSegment is not null
                && PresentationChecklistEvidencePolicy.AllowsCompletion(
                    template,
                    checklistItem,
                    candidate.EvidenceSegment)
                && IsCompletionEvidenceEligible(
                    checklistItem.CompletionEligibleFromTranscriptIndex,
                    candidate.EvidenceSegment.Id,
                    transcriptIndexById))
            .GroupBy(candidate => candidate.Evaluation!.ChecklistItemId)
            .ToDictionary(
                group => group.Key,
                group =>
                {
                    var ordered = group
                    .OrderByDescending(candidate => candidate.Evaluation!.Confidence)
                    .DistinctBy(candidate => (
                        candidate.EvidenceSegment!.Id,
                        candidate.Evaluation!.EvidenceQuote.Trim()))
                    .ToArray();
                    return currentById.TryGetValue(group.Key, out var item)
                        && HeuristicConversationCoachAgent.RequiresCompoundEvidence(item)
                            ? ordered
                            : ordered.Take(1).ToArray();
                });

        return current.Select(item =>
        {
            if (item.Status == ChecklistItemStatus.Completed
                || !byItem.TryGetValue(item.Id, out var candidates))
            {
                return item;
            }

            var primaryEvaluation = candidates[0].Evaluation!;
            var evidence = candidates
                .Select(candidate => new ChecklistEvidence(
                    candidate.EvidenceSegment!.Id,
                    candidate.EvidenceSegment.Speaker,
                    candidate.Evaluation!.EvidenceQuote.Trim(),
                    candidate.EvidenceSegment.OccurredAtUtc,
                    Math.Clamp(candidate.Evaluation.Confidence, 0, 1)))
                .ToArray();

            return item with
            {
                Status = ChecklistItemStatus.Completed,
                AutoCompleted = true,
                Confidence = evidence.Min(entry => entry.Confidence),
                CompletionReason = primaryEvaluation.Reason.Trim(),
                CompletedAtUtc = DateTimeOffset.UtcNow,
                Evidence = item.Evidence
                    .Concat(evidence)
                    .DistinctBy(entry => (entry.TranscriptSegmentId, entry.Quote))
                    .TakeLast(20)
                    .ToArray()
            };
        }).ToArray();
    }

    private static IReadOnlyList<RecommendedTaskState> ApplyRecommendations(
        IReadOnlyList<RecommendedTaskState> current,
        SessionTemplateKind template,
        IReadOnlyList<RecommendedTaskProposal> proposals,
        IReadOnlyList<TranscriptSegment> transcript,
        IReadOnlyList<ChecklistItemState> checklist,
        MeetingPurpose purpose,
        IReadOnlyCollection<Guid> knowledgeSourceIds,
        ICollection<string> warnings)
    {
        var segmentIds = transcript.Select(segment => segment.Id).ToHashSet();
        var result = RecommendationIntentPolicy
            .Consolidate(template, checklist, current)
            .ToList();
        var knownTitles = result
            .Select(task => HeuristicConversationCoachAgent.Normalize(task.Title))
            .ToHashSet(StringComparer.Ordinal);
        var checklistIntents = checklist
            .Select(item => RecommendationIntentPolicy.Resolve(
                template,
                item.Title,
                item.CompletionCriteria))
            .Where(intent => intent is not null)
            .Cast<string>()
            .ToHashSet(StringComparer.Ordinal);
        var coveredContext = checklist
            .SelectMany(item => new[] { item.Title, item.CompletionCriteria })
            .Concat(purpose.SuccessCriteria)
            .Select(HeuristicConversationCoachAgent.Normalize)
            .Where(value => value.Length > 0)
            .ToArray();

        foreach (var proposal in proposals)
        {
            if (proposal is null
                || string.IsNullOrWhiteSpace(proposal.Title)
                || string.IsNullOrWhiteSpace(proposal.Rationale)
                || !double.IsFinite(proposal.Confidence)
                || proposal.Confidence is < 0.70 or > 1)
            {
                warnings.Add(
                    "Rejected a recommended task because its title, rationale, or confidence was invalid.");
                continue;
            }

            if (proposal.SourceTranscriptSegmentIds is null
                || proposal.SourceTranscriptSegmentIds.Count == 0
                || proposal.SourceTranscriptSegmentIds.Any(id => !segmentIds.Contains(id)))
            {
                warnings.Add(
                    $"Rejected recommended task '{proposal.Title}': transcript evidence was missing.");
                continue;
            }

            var normalizedTitle = HeuristicConversationCoachAgent.Normalize(proposal.Title);
            var intentKey = RecommendationIntentPolicy.Resolve(
                template,
                proposal.Title,
                proposal.Rationale);
            if (intentKey is not null)
            {
                if (checklistIntents.Contains(intentKey))
                {
                    continue;
                }

                var sameIntent = result
                    .Where(task => string.Equals(
                        RecommendationIntentPolicy.Resolve(template, task),
                        intentKey,
                        StringComparison.Ordinal))
                    .ToArray();
                if (sameIntent.Any(task => task.Status != RecommendationStatus.Proposed))
                {
                    continue;
                }

                var existing = sameIntent
                    .Where(task => task.Status == RecommendationStatus.Proposed)
                    .ToArray();
                if (existing.Length > 0)
                {
                    var candidate = new RecommendedTaskState(
                        Guid.NewGuid(),
                        proposal.Title.Trim(),
                        proposal.Rationale.Trim(),
                        Math.Clamp(proposal.Confidence, 0, 1),
                        proposal.SourceTranscriptSegmentIds.Distinct().ToArray(),
                        RecommendationStatus.Proposed,
                        DateTimeOffset.UtcNow,
                        Evidence: [])
                    {
                        IntentKey = intentKey,
                        WordingSourceTranscriptSegmentIds =
                            proposal.SourceTranscriptSegmentIds
                                .Distinct()
                                .TakeLast(20)
                                .ToArray(),
                        KnowledgeSourceIds = knowledgeSourceIds.Distinct().ToArray()
                    };
                    var merged = RecommendationIntentPolicy.Merge(
                        [.. existing, candidate],
                        intentKey);
                    var retainedIndex = result.FindIndex(task => task.Id == merged.Id);
                    result.RemoveAll(task => existing.Any(existingTask =>
                        existingTask.Id == task.Id));
                    result.Insert(
                        Math.Min(result.Count, Math.Max(0, retainedIndex)),
                        merged);
                    knownTitles.Add(
                        HeuristicConversationCoachAgent.Normalize(merged.Title));
                    continue;
                }
            }

            if (knownTitles.Contains(normalizedTitle)
                || coveredContext.Any(context =>
                    PresentationCoachingPolicy.HasSubstantialOverlap(
                        normalizedTitle,
                        context)))
            {
                continue;
            }

            knownTitles.Add(normalizedTitle);
            result.Add(new RecommendedTaskState(
                Guid.NewGuid(),
                proposal.Title.Trim(),
                proposal.Rationale.Trim(),
                Math.Clamp(proposal.Confidence, 0, 1),
                proposal.SourceTranscriptSegmentIds.Distinct().ToArray(),
                RecommendationStatus.Proposed,
                DateTimeOffset.UtcNow,
                Evidence: [])
            {
                IntentKey = intentKey,
                WordingSourceTranscriptSegmentIds =
                    proposal.SourceTranscriptSegmentIds
                        .Distinct()
                        .TakeLast(20)
                        .ToArray(),
                KnowledgeSourceIds = knowledgeSourceIds.Distinct().ToArray()
            });
        }

        return RecommendationIntentPolicy.Consolidate(
            template,
            checklist,
            result);
    }

    private ContextualCardApplicationResult ApplyContextualCards(
        MeetingSessionState session,
        IReadOnlyList<ContextualCardProposal> proposals,
        IReadOnlyList<TranscriptSegment> analysisWindow,
        IReadOnlyList<SessionKnowledgeSnippet> knowledge)
    {
        var analysisWindowById = analysisWindow.ToDictionary(segment => segment.Id);
        var result = FilterClientReadyCards(session.ContextualCards)
            .TakeLast(MaximumRetainedContextualCards)
            .ToList();
        var shownDefinitionKeys = new HashSet<string>(
            session.ShownDefinitionKeys,
            StringComparer.Ordinal);
        var shownHintKeys = new HashSet<string>(
            session.ShownHintKeys,
            StringComparer.Ordinal);
        var definitionCooldowns = new Dictionary<string, DateTimeOffset>(
            session.DefinitionCooldowns,
            StringComparer.Ordinal);
        var hintCooldowns = new Dictionary<string, DateTimeOffset>(
            session.HintCooldowns,
            StringComparer.Ordinal);
        var contentFingerprints = new HashSet<string>(
            session.ContentFingerprints,
            StringComparer.Ordinal);
        var diagnostics = new List<AlertDiagnostic>();
        var latestFinalSegmentIndex = session.Transcript.Count(segment => segment.IsFinal) - 1;
        var now = _timeProvider.GetUtcNow();
        var candidates = new List<AlertCandidate>();

        foreach (var proposal in proposals)
        {
            if (proposal is null)
            {
                diagnostics.Add(new AlertDiagnostic(
                    ContextualCardKind.Definition,
                    string.Empty,
                    AlertRejectionReason.MissingEvidence,
                    "proposal was null"));
                continue;
            }

            if (!Enum.IsDefined(proposal.Kind))
            {
                diagnostics.Add(new AlertDiagnostic(
                    proposal.Kind,
                    proposal.ConceptKey ?? string.Empty,
                    AlertRejectionReason.UnknownKind,
                    "proposal kind was outside the supported definition and hint values"));
                continue;
            }

            if (string.IsNullOrWhiteSpace(proposal.Title)
                || proposal.Title.Trim().Length > 80
                || string.IsNullOrWhiteSpace(proposal.Content)
                || proposal.Content.Trim().Length > 320
                || !double.IsFinite(proposal.Confidence)
                || proposal.Confidence is < ContextualCardThreshold or > 1)
            {
                diagnostics.Add(new AlertDiagnostic(
                    proposal.Kind,
                    proposal.ConceptKey ?? string.Empty,
                    AlertRejectionReason.MissingEvidence,
                    "proposal title, content, or confidence was invalid"));
                continue;
            }

            if (ContainsSalesOrRecommendationLanguage(proposal.Title, proposal.Content)
                || !PresentationCoachingPolicy.IsClientReadyExplanation(
                    proposal.Title,
                    proposal.Content))
            {
                diagnostics.Add(new AlertDiagnostic(
                    proposal.Kind,
                    proposal.ConceptKey ?? string.Empty,
                    AlertRejectionReason.SalesOrRecommendationContent,
                    "proposal content included recommendation, action, or presenter-directed language"));
                continue;
            }

            if (proposal.SourceTranscriptSegmentIds is not { Count: > 0 }
                || proposal.SourceTranscriptSegmentIds.Any(
                    id => !analysisWindowById.ContainsKey(id))
                || !PresentationCoachingPolicy.IsTitleGrounded(
                    proposal.Title,
                    proposal.SourceTranscriptSegmentIds,
                    analysisWindow))
            {
                diagnostics.Add(new AlertDiagnostic(
                    proposal.Kind,
                    proposal.ConceptKey ?? string.Empty,
                    AlertRejectionReason.MissingEvidence,
                    "proposal title or evidence sources were not grounded in the final transcript window"));
                continue;
            }

            EducationalConcept? concept = null;
            var sourceKnowledgeIds = Array.Empty<Guid>();
            string conceptKey;
            ConceptCategory category;
            bool requiresAzureVendorScope;
            AlertRejectionReason reason;
            string details;
            if (EducationalConceptCatalog.TryResolveByAliasOrTitle(
                    proposal.ConceptKey ?? proposal.Title,
                    out _))
            {
                if (!PresentationCoachingPolicy.TryResolveEducationalProposal(
                        proposal,
                        analysisWindow,
                        session.Purpose,
                        out concept,
                        out reason,
                        out details))
                {
                    diagnostics.Add(new AlertDiagnostic(
                        proposal.Kind,
                        proposal.ConceptKey ?? proposal.Title,
                        reason == AlertRejectionReason.None
                            ? AlertRejectionReason.MentionNotFound
                            : reason,
                        string.IsNullOrWhiteSpace(details)
                            ? "proposal could not be mapped to a grounded educational concept"
                            : details));
                    continue;
                }

                conceptKey = concept!.ConceptKey;
                category = concept.Category;
                requiresAzureVendorScope = concept.RequiresAzureVendorScope;
                if (!AudienceFamiliarityPolicy.IncludeCatalogConcept(
                        concept,
                        session.AudienceFamiliarity)
                    && !(session.AudienceFamiliarity == AudienceFamiliarity.Expert
                        && proposal.Kind == ContextualCardKind.Hint))
                {
                    diagnostics.Add(new AlertDiagnostic(
                        proposal.Kind,
                        conceptKey,
                        AlertRejectionReason.MentionNotFound,
                        "catalog concept is suppressed for the current audience familiarity"));
                    continue;
                }
            }
            else
            {
                if (!TryValidateKnowledgeGrounding(
                        proposal,
                        session,
                        knowledge,
                        out sourceKnowledgeIds,
                        out details))
                {
                    diagnostics.Add(new AlertDiagnostic(
                        proposal.Kind,
                        proposal.ConceptKey ?? proposal.Title,
                        AlertRejectionReason.MissingEvidence,
                        details));
                    continue;
                }

                conceptKey = CreateKnowledgeConceptKey(proposal.Title);
                category = ConceptCategory.DomainSpecific;
                requiresAzureVendorScope = false;
            }

            var fingerprint = CreateContentFingerprint(
                proposal.Title,
                proposal.Content,
                proposal.Kind);
            if (contentFingerprints.Contains(fingerprint))
            {
                diagnostics.Add(new AlertDiagnostic(
                    proposal.Kind,
                    conceptKey,
                    AlertRejectionReason.ContentFingerprint,
                    "proposal matched previously shown alert wording"));
                continue;
            }

            if (!TryValidateLifecycle(
                    proposal.Kind,
                    conceptKey,
                    latestFinalSegmentIndex,
                    session.LastEducationalCardSegmentIndex,
                    now,
                    shownDefinitionKeys,
                    definitionCooldowns,
                    hintCooldowns,
                    session.AudienceFamiliarity,
                    out reason,
                    out details))
            {
                diagnostics.Add(new AlertDiagnostic(
                    proposal.Kind,
                    conceptKey,
                    reason,
                    details));
                continue;
            }

            candidates.Add(new AlertCandidate(
                conceptKey,
                proposal.Kind,
                proposal.Title.Trim(),
                proposal.Content.Trim(),
                proposal.Confidence,
                proposal.SourceTranscriptSegmentIds.Distinct().ToArray(),
                proposal.SourceTranscriptSegmentIds
                    .Select(id => analysisWindow
                        .Select((segment, index) => (segment.Id, Index: index))
                        .First(item => item.Id == id).Index)
                    .DefaultIfEmpty(0)
                    .Max(),
                category,
                requiresAzureVendorScope,
                sourceKnowledgeIds.Length == 0 ? "catalog" : "session-knowledge",
                sourceKnowledgeIds));
        }

        var ranked = AlertRanker.Rank(
            candidates,
            result,
            shownDefinitionKeys,
            shownHintKeys);
        foreach (var rejected in ranked.Skip(MaximumContextualCardsPerAnalysis))
        {
            diagnostics.Add(new AlertDiagnostic(
                rejected.Candidate.Kind,
                rejected.Candidate.ConceptKey,
                AlertRejectionReason.InsufficientRanking,
                "another eligible educational alert ranked higher for this transcript update"));
        }

        var winner = ranked.FirstOrDefault();
        if (winner is not null)
        {
            var fingerprint = CreateContentFingerprint(
                winner.Candidate.Title,
                winner.Candidate.Content,
                winner.Candidate.Kind);
            result.Add(new ContextualCardState(
                Guid.NewGuid(),
                winner.Candidate.Kind,
                winner.Candidate.Title,
                winner.Candidate.Content,
                winner.Candidate.Confidence,
                winner.Candidate.SourceTranscriptSegmentIds,
                now)
            {
                ConceptKey = winner.Candidate.ConceptKey,
                MemberAlertStatus = ResolveMemberAlertStatus(
                    session,
                    winner.Candidate.Kind,
                    winner.Candidate.SourceKnowledgeIds),
                KnowledgeSourceIds = winner.Candidate.SourceKnowledgeIds
            });

            if (winner.Candidate.Kind == ContextualCardKind.Definition)
            {
                shownDefinitionKeys.Add(winner.Candidate.ConceptKey);
                definitionCooldowns[winner.Candidate.ConceptKey] = now;
            }

            else
            {
                shownHintKeys.Add(winner.Candidate.ConceptKey);
                hintCooldowns[winner.Candidate.ConceptKey] = now;
            }

            contentFingerprints.Add(fingerprint);
            diagnostics.Add(new AlertDiagnostic(
                winner.Candidate.Kind,
                winner.Candidate.ConceptKey,
                AlertRejectionReason.None,
                "educational alert accepted"));
            session = session with { LastEducationalCardSegmentIndex = latestFinalSegmentIndex };
        }

        LogAlertDiagnostics(session.Id, diagnostics);
        return new ContextualCardApplicationResult(
            result.TakeLast(MaximumRetainedContextualCards).ToArray(),
            shownDefinitionKeys,
            shownHintKeys,
            definitionCooldowns,
            hintCooldowns,
            contentFingerprints,
            session.LastEducationalCardSegmentIndex);
    }

    private static MemberAlertStatus ResolveMemberAlertStatus(
        MeetingSessionState session,
        ContextualCardKind kind,
        IReadOnlyList<Guid> sourceKnowledgeIds)
    {
        return session.MemberAlertMode switch
        {
            MemberAlertDeliveryMode.Manual => MemberAlertStatus.PendingApproval,
            MemberAlertDeliveryMode.Automatic => MemberAlertStatus.Published,
            MemberAlertDeliveryMode.SafeAutomatic
                when kind == ContextualCardKind.Definition
                && (sourceKnowledgeIds.Count == 0
                    ? session.KnowledgeSources.All(source =>
                        source.Visibility == KnowledgeSourceVisibility.MemberEligible)
                    : sourceKnowledgeIds.All(sourceId =>
                        session.KnowledgeSources.Any(source =>
                            source.Id == sourceId
                            && source.Status == KnowledgeSourceStatus.Ready
                            && source.Visibility == KnowledgeSourceVisibility.MemberEligible))) =>
                MemberAlertStatus.Published,
            MemberAlertDeliveryMode.SafeAutomatic => MemberAlertStatus.PendingApproval,
            _ => MemberAlertStatus.PendingApproval
        };
    }

    private static bool HasSameKnowledgeAuthorization(
        MeetingSessionState session,
        IReadOnlyDictionary<Guid, KnowledgeSourceVisibility> expected)
    {
        var current = session.KnowledgeSources
            .Where(source => source.Status == KnowledgeSourceStatus.Ready)
            .ToDictionary(source => source.Id, source => source.Visibility);
        return current.Count == expected.Count
            && expected.All(pair =>
                current.TryGetValue(pair.Key, out var visibility)
                && visibility == pair.Value);
    }

    private static IReadOnlyList<ContextualCardState> FilterClientReadyCards(
        IReadOnlyList<ContextualCardState> cards)
    {
        return cards
            .Where(card => PresentationCoachingPolicy.IsClientReadyExplanation(
                card.Title,
                card.Content))
            .ToArray();
    }

    private static bool TryValidateLifecycle(
        ContextualCardKind kind,
        string conceptKey,
        int latestFinalSegmentIndex,
        int? lastEducationalCardSegmentIndex,
        DateTimeOffset now,
        IReadOnlySet<string> shownDefinitionKeys,
        IReadOnlyDictionary<string, DateTimeOffset> definitionCooldowns,
        IReadOnlyDictionary<string, DateTimeOffset> hintCooldowns,
        AudienceFamiliarity audienceFamiliarity,
        out AlertRejectionReason reason,
        out string details)
    {
        if (lastEducationalCardSegmentIndex == latestFinalSegmentIndex)
        {
            reason = AlertRejectionReason.CooldownActive;
            details = "an educational alert was already shown for this final transcript segment";
            return false;
        }

        if (kind == ContextualCardKind.Definition)
        {
            if (shownDefinitionKeys.Contains(conceptKey))
            {
                reason = AlertRejectionReason.CooldownActive;
                details = "definition cards are shown once per concept per session";
                return false;
            }

            reason = AlertRejectionReason.None;
            details = string.Empty;
            return true;
        }

        if (!shownDefinitionKeys.Contains(conceptKey)
            && audienceFamiliarity != AudienceFamiliarity.Expert)
        {
            reason = AlertRejectionReason.CooldownActive;
            details = "hint cards require a previously shown definition for the same concept";
            return false;
        }

        if (definitionCooldowns.TryGetValue(conceptKey, out var definitionShownAt)
            && now - definitionShownAt < DefinitionToHintCooldown)
        {
            reason = AlertRejectionReason.CooldownActive;
            details = "the definition-to-hint cooldown is still active";
            return false;
        }

        if (hintCooldowns.TryGetValue(conceptKey, out var hintShownAt)
            && now - hintShownAt < HintRepeatCooldown)
        {
            reason = AlertRejectionReason.CooldownActive;
            details = "the hint repeat cooldown is still active";
            return false;
        }

        reason = AlertRejectionReason.None;
        details = string.Empty;
        return true;
    }

    private static bool ContainsSalesOrRecommendationLanguage(
        string title,
        string content)
    {
        var normalized = HeuristicConversationCoachAgent.Normalize(
            $"{title} {content}");
        return SalesOrRecommendationPatterns.Any(pattern =>
            normalized.Contains(
                HeuristicConversationCoachAgent.Normalize(pattern),
                StringComparison.Ordinal));
    }

    private static string CreateContentFingerprint(
        string title,
        string content,
        ContextualCardKind kind)
    {
        var normalized = HeuristicConversationCoachAgent.Normalize(
            $"{kind} {title} {content}");
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return Convert.ToHexString(hash);
    }

    private static bool TryValidateKnowledgeGrounding(
        ContextualCardProposal proposal,
        MeetingSessionState session,
        IReadOnlyList<SessionKnowledgeSnippet> knowledge,
        out Guid[] sourceKnowledgeIds,
        out string details)
    {
        sourceKnowledgeIds = [];
        var requestedIds = (proposal.SourceKnowledgeIds ?? [])
            .Distinct()
            .ToArray();
        if (requestedIds.Length == 0
            || string.IsNullOrWhiteSpace(proposal.KnowledgeEvidenceQuote))
        {
            details = "domain-specific cards require member-eligible knowledge IDs and an exact supporting quote";
            return false;
        }

        var memberEligibleIds = session.KnowledgeSources
            .Where(source => source.Status == KnowledgeSourceStatus.Ready
                && source.Visibility == KnowledgeSourceVisibility.MemberEligible)
            .Select(source => source.Id)
            .ToHashSet();
        if (requestedIds.Any(id => !memberEligibleIds.Contains(id)))
        {
            details = "a domain-specific card referenced knowledge that was not member-eligible";
            return false;
        }

        var snippets = knowledge
            .Where(item => requestedIds.Contains(item.SourceId)
                && item.Visibility == KnowledgeSourceVisibility.MemberEligible)
            .ToDictionary(item => item.SourceId);
        if (snippets.Count != requestedIds.Length)
        {
            details = "a domain-specific card referenced unavailable session knowledge";
            return false;
        }

        var evidence = NormalizeEvidenceText(proposal.KnowledgeEvidenceQuote);
        if (evidence.Length < 12
            || !snippets.Values.Any(item => NormalizeEvidenceText(item.Content)
                .Contains(evidence, StringComparison.OrdinalIgnoreCase)))
        {
            details = "the knowledge evidence quote was not an exact excerpt from a member-eligible source";
            return false;
        }

        sourceKnowledgeIds = requestedIds;
        details = string.Empty;
        return true;
    }

    private static string NormalizeEvidenceText(string value) =>
        string.Join(' ', value.Split(
            (char[]?)null,
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    private static string CreateKnowledgeConceptKey(string title)
    {
        var normalized = HeuristicConversationCoachAgent.Normalize(title);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return $"knowledge:{Convert.ToHexString(hash)[..16]}";
    }

    private void LogAlertDiagnostics(
        Guid sessionId,
        IEnumerable<AlertDiagnostic> diagnostics)
    {
        foreach (var diagnostic in diagnostics)
        {
            _logger?.LogDebug(
                "Educational alert diagnostic for session {SessionId}: Kind={Kind} ConceptKey={ConceptKey} Reason={Reason} Details={Details}",
                sessionId,
                diagnostic.Kind,
                diagnostic.ConceptKey,
                diagnostic.Reason,
                diagnostic.Details);
        }
    }

    private static IReadOnlyList<string> NormalizeWarnings(IEnumerable<string> warnings)
    {
        return warnings
            .Where(warning => !string.IsNullOrWhiteSpace(warning))
            .Distinct(StringComparer.Ordinal)
            .TakeLast(20)
            .ToArray();
    }

    private static IReadOnlyList<RecommendedTaskState> ApplyRecommendationEvaluations(
        IReadOnlyList<RecommendedTaskState> current,
        IReadOnlyList<RecommendationEvaluation> evaluations,
        IReadOnlyList<TranscriptSegment> analysisWindow,
        TranscriptSegment latestSegment,
        IReadOnlyList<TranscriptSegment> finalTranscript,
        ICollection<string> warnings)
    {
        var knownIds = current.Select(item => item.Id).ToHashSet();
        var acceptedById = current
            .Where(item => item.Status == RecommendationStatus.Accepted)
            .ToDictionary(item => item.Id);
        var transcriptIndexById = finalTranscript
            .Select((segment, index) => (segment.Id, Index: index))
            .ToDictionary(item => item.Id, item => item.Index);
        foreach (var evaluation in evaluations)
        {
            if (evaluation is null)
            {
                warnings.Add("Rejected an empty recommendation evaluation.");
                continue;
            }

            if (!knownIds.Contains(evaluation.RecommendationId))
            {
                warnings.Add(
                    $"Rejected recommendation completion for unknown recommendation {evaluation.RecommendationId}.");
                continue;
            }

            if (evaluation.ShouldComplete
                && (!double.IsFinite(evaluation.Confidence)
                    || evaluation.Confidence is < 0 or > 1
                    || string.IsNullOrWhiteSpace(evaluation.Reason)
                    || string.IsNullOrWhiteSpace(evaluation.EvidenceQuote)))
            {
                warnings.Add(
                    $"Rejected recommendation completion {evaluation.RecommendationId}: evaluation data was invalid.");
            }
            else if (evaluation.ShouldComplete
                && ResolveEvidenceSegment(
                    evaluation.SourceTranscriptSegmentId,
                    evaluation.EvidenceQuote,
                    analysisWindow,
                    latestSegment) is null)
            {
                warnings.Add(
                    $"Rejected recommendation completion {evaluation.RecommendationId}: agent evidence was not present in its cited analysis-window segment.");
            }
        }

        var byItem = evaluations
            .Select(evaluation => (
                Evaluation: evaluation,
                EvidenceSegment: evaluation is null
                    ? null
                    : ResolveEvidenceSegment(
                        evaluation.SourceTranscriptSegmentId,
                        evaluation.EvidenceQuote,
                        analysisWindow,
                        latestSegment)))
            .Where(candidate => candidate.Evaluation is not null
                && acceptedById.TryGetValue(
                    candidate.Evaluation.RecommendationId,
                    out var recommendation)
                && candidate.Evaluation.ShouldComplete
                && double.IsFinite(candidate.Evaluation.Confidence)
                && candidate.Evaluation.Confidence is >= AutoCompletionThreshold and <= 1
                && !string.IsNullOrWhiteSpace(candidate.Evaluation.Reason)
                && candidate.EvidenceSegment is not null
                && recommendation.CompletionEligibleFromTranscriptIndex is { } eligibleFrom
                && IsCompletionEvidenceEligible(
                    eligibleFrom,
                    candidate.EvidenceSegment.Id,
                    transcriptIndexById)
                && !recommendation.SourceTranscriptSegmentIds.Contains(
                    candidate.EvidenceSegment.Id))
            .GroupBy(candidate => candidate.Evaluation!.RecommendationId)
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderByDescending(candidate => candidate.Evaluation!.Confidence)
                    .First());

        return current.Select(item =>
        {
            if (item.Status != RecommendationStatus.Accepted
                || !byItem.TryGetValue(item.Id, out var candidate))
            {
                return item;
            }

            var evaluation = candidate.Evaluation!;
            var evidenceSegment = candidate.EvidenceSegment!;
            if (item.AcceptedAtUtc is null)
            {
                return item;
            }

            var evidence = new ChecklistEvidence(
                evidenceSegment.Id,
                evidenceSegment.Speaker,
                evaluation.EvidenceQuote,
                evidenceSegment.OccurredAtUtc,
                Math.Clamp(evaluation.Confidence, 0, 1));
            return item with
            {
                Status = RecommendationStatus.Completed,
                CompletedAtUtc = DateTimeOffset.UtcNow,
                CompletionReason = evaluation.Reason.Trim(),
                Evidence = (item.Evidence ?? []).Append(evidence).TakeLast(5).ToArray()
            };
        }).ToArray();
    }

    private static TranscriptSegment? ResolveEvidenceSegment(
        Guid? sourceTranscriptSegmentId,
        string evidenceQuote,
        IReadOnlyList<TranscriptSegment> analysisWindow,
        TranscriptSegment latestSegment)
    {
        if (string.IsNullOrWhiteSpace(evidenceQuote))
        {
            return null;
        }

        var evidenceSegmentId = sourceTranscriptSegmentId ?? latestSegment.Id;
        return analysisWindow.FirstOrDefault(segment =>
            segment.Id == evidenceSegmentId
            && segment.Text.Contains(evidenceQuote, StringComparison.Ordinal));
    }

    private static bool IsCompletionEvidenceEligible(
        int? completionEligibleFromTranscriptIndex,
        Guid evidenceSegmentId,
        IReadOnlyDictionary<Guid, int> transcriptIndexById)
    {
        return completionEligibleFromTranscriptIndex is null
            || transcriptIndexById.TryGetValue(evidenceSegmentId, out var evidenceIndex)
            && evidenceIndex >= completionEligibleFromTranscriptIndex;
    }

    private static void ValidatePurpose(MeetingPurpose purpose)
    {
        ArgumentNullException.ThrowIfNull(purpose);

        if (string.IsNullOrWhiteSpace(purpose.Title)
            || string.IsNullOrWhiteSpace(purpose.MeetingType)
            || string.IsNullOrWhiteSpace(purpose.Objective)
            || purpose.SuccessCriteria is null)
        {
            throw new ArgumentException(
                "Meeting title, type, objective, and success criteria are required.");
        }
    }

    private void EnsureActive(MeetingSessionState session)
    {
        EnsureNotExpired(session);
        if (session.Status != MeetingSessionStatus.Active)
        {
            throw new InvalidOperationException("The meeting session is already completed.");
        }
    }

    private void EnsureNotExpired(MeetingSessionState session)
    {
        if (SessionLifecycle.IsExpired(session, _timeProvider.GetUtcNow()))
        {
            throw new SessionExpiredException(session.Id);
        }
    }

    private static void ValidateTemplate(SessionTemplateKind template)
    {
        if (!Enum.IsDefined(template))
        {
            throw new ArgumentException("A supported session template is required.");
        }
    }

    private static void ValidateMemberAlertMode(MemberAlertDeliveryMode mode)
    {
        if (!Enum.IsDefined(mode))
        {
            throw new ArgumentException("A supported member alert mode is required.");
        }
    }

    private static void ValidateAudienceFamiliarity(AudienceFamiliarity familiarity)
    {
        if (!Enum.IsDefined(familiarity))
        {
            throw new ArgumentException("A supported audience familiarity is required.");
        }
    }

    private static string NormalizeParticipantName(string? displayName, string fallback)
    {
        var normalized = string.IsNullOrWhiteSpace(displayName)
            ? fallback
            : displayName.Trim();
        if (normalized.Length > 80)
        {
            throw new ArgumentException("Participant display name cannot exceed 80 characters.");
        }

        return normalized;
    }

    private static string? NormalizeMeetingId(string? meetingId)
    {
        if (string.IsNullOrWhiteSpace(meetingId))
        {
            return null;
        }

        var normalized = meetingId.Trim();
        if (normalized.Length > 512)
        {
            throw new ArgumentException("Teams meeting ID cannot exceed 512 characters.");
        }

        return normalized;
    }

    private MeetingSessionState ApplyRuntimeAnalysisState(MeetingSessionState session)
    {
        return session with
        {
            IsAnalyzing = session.Status == MeetingSessionStatus.Active
                && IsAnalysisPending(session.Id)
        };
    }

    private bool IsAnalysisPending(Guid sessionId)
    {
        return _analysisStates.TryGetValue(sessionId, out var state)
            && state.IsAnalyzing;
    }

    private sealed record ContextualCardApplicationResult(
        IReadOnlyList<ContextualCardState> Cards,
        HashSet<string> ShownDefinitionKeys,
        HashSet<string> ShownHintKeys,
        Dictionary<string, DateTimeOffset> DefinitionCooldowns,
        Dictionary<string, DateTimeOffset> HintCooldowns,
        HashSet<string> ContentFingerprints,
        int? LastEducationalCardSegmentIndex);

    private sealed class SessionAnalysisState : IDisposable
    {
        private readonly Channel<long> _trigger = Channel.CreateBounded<long>(
            new BoundedChannelOptions(1)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleWriter = false,
                SingleReader = true
            });
        private long _latestRequestedGeneration;
        private long _completedGeneration;
        private readonly CancellationTokenSource _cancellation = new();
        private readonly CancellationToken _cancellationToken;
        private readonly object _workerLock = new();
        private int _disposed;

        public SessionAnalysisState()
        {
            _cancellationToken = _cancellation.Token;
        }

        public Task? BackgroundTask;

        public ChannelReader<long> TriggerReader => _trigger.Reader;
        public CancellationToken CancellationToken => _cancellationToken;

        public bool IsAnalyzing =>
            Volatile.Read(ref _latestRequestedGeneration)
            > Volatile.Read(ref _completedGeneration);

        public long LatestRequestedGeneration =>
            Volatile.Read(ref _latestRequestedGeneration);

        public void Signal()
        {
            var generation = Interlocked.Increment(ref _latestRequestedGeneration);
            _trigger.Writer.TryWrite(generation);
        }

        public void EnsureBackgroundTask(Func<Task> taskFactory)
        {
            lock (_workerLock)
            {
                if (BackgroundTask is null || BackgroundTask.IsCompleted)
                {
                    BackgroundTask = taskFactory();
                }
            }
        }

        public void MarkCompleted(long generation)
        {
            while (true)
            {
                var current = Volatile.Read(ref _completedGeneration);
                if (generation <= current
                    || Interlocked.CompareExchange(
                        ref _completedGeneration,
                        generation,
                        current) == current)
                {
                    return;
                }
            }
        }

        public void Cancel() => _cancellation.Cancel();

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            _cancellation.Cancel();
            _trigger.Writer.TryComplete();
            _cancellation.Dispose();
        }
    }
}
