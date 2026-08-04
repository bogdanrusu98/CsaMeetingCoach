using System.Collections.Concurrent;
using System.Threading.Channels;
using CsaMeetingCoach.Contracts;
using Microsoft.Extensions.Logging;

namespace CsaMeetingCoach.Core;

public sealed class MeetingSessionCoordinator : IDisposable
{
    public const double AutoCompletionThreshold = 0.82;
    private const double ContextualCardThreshold = 0.75;
    private const int MaximumContextualCardsPerAnalysis = 2;
    private const int MaximumRetainedContextualCards = 12;
    private const string AnalysisUnavailableWarning =
        "AI coaching is temporarily unavailable. Transcript and local checklist processing continued.";

    private readonly IMeetingSessionStore _store;
    private readonly IMeetingChecklistPlanner _checklistPlanner;
    private readonly IConversationCoachAgent _deterministicAgent;
    private readonly IConversationCoachAgent? _aiAgent;
    private readonly ISessionUpdatePublisher _updatePublisher;
    private readonly ILogger<MeetingSessionCoordinator>? _logger;
    private readonly AnalysisOptions _analysisOptions;
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
        AnalysisOptions? analysisOptions = null)
    {
        _store = store;
        _checklistPlanner = checklistPlanner;
        _deterministicAgent = deterministicAgent;
        _aiAgent = aiAgent;
        _updatePublisher = updatePublisher;
        _logger = logger;
        _analysisOptions = analysisOptions ?? AnalysisOptions.Default;
        _analysisOptions.Validate();
    }


    public async Task<MeetingSessionState> CreateAsync(
        CreateMeetingSessionRequest request,
        CancellationToken cancellationToken)
    {
        ValidatePurpose(request.Purpose);
        var now = DateTimeOffset.UtcNow;
        var session = new MeetingSessionState(
            Guid.NewGuid(),
            request.Purpose,
            MeetingSessionStatus.Active,
            now,
            now,
            Revision: 1,
            _checklistPlanner.CreateChecklist(request.Purpose, request.Checklist),
            Transcript: [],
            RecommendedTasks: [],
            Warnings: [],
            TeamsOnlineMeetingId: NormalizeMeetingId(request.TeamsOnlineMeetingId),
            StateSchemaVersion: MeetingSessionState.CurrentSchemaVersion);

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
                segment = new TranscriptSegment(
                    Guid.NewGuid(),
                    request.Speaker.Trim(),
                    request.Text.Trim(),
                    request.OccurredAtUtc ?? DateTimeOffset.UtcNow,
                    request.IsFinal,
                    request.SourceSegmentId);

                transcript = session.Transcript.Append(segment).ToArray();
                transcriptUpdate = session with
                {
                    Transcript = transcript,
                    Revision = session.Revision + 1,
                    UpdatedAtUtc = DateTimeOffset.UtcNow
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
            var context = new CoachAgentContext(
                transcriptUpdate.Purpose,
                transcriptUpdate.Checklist,
                finalTranscript,
                transcriptUpdate.RecommendedTasks,
                transcriptUpdate.ContextualCards);
            var deterministicDecision = await _deterministicAgent.AnalyzeAsync(
                context, segment, cancellationToken);
            var fastLaneDecision = _aiAgent is null
                ? deterministicDecision
                : deterministicDecision with
                {
                    RecommendedTasks = [],
                    ContextualCards = PresentationCoachingPolicy.SelectContextualCards(
                        context,
                        [],
                        analysisWindow)
                };
            var decision = CrossCuttingRecommendationPolicy.Apply(fastLaneDecision, segment);

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
                decision.RecommendedTasks,
                finalTranscript,
                transcriptUpdate.Checklist,
                transcriptUpdate.Purpose,
                warnings);
            var contextualCards = ApplyContextualCards(
                transcriptUpdate.ContextualCards,
                decision.ContextualCards,
                analysisWindow);
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
                ContextualCards = contextualCards,
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
        if (state.BackgroundTask is null || state.BackgroundTask.IsCompleted)
        {
            state.BackgroundTask = Task.Run(
                () => RunAnalysisLoopAsync(sessionId, state, _disposalCts.Token));
        }

        state.Signal();
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

                if (loaded.Status != MeetingSessionStatus.Active)
                {
                    state.MarkCompleted(analysisGeneration);
                    await ClearAnalyzingFlagAsync(loaded, cancellationToken);
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
                context = new CoachAgentContext(
                    session.Purpose,
                    session.Checklist,
                    finalTranscript,
                    session.RecommendedTasks,
                    session.ContextualCards);
            }
            finally
            {
                loadGate.Release();
            }
        }

        // Call AI with a bounded timeout.
        CoachAgentDecision aiDecision;
        using var aiCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        aiCts.CancelAfter(_analysisOptions.AiTimeout);
        try
        {
            aiDecision = await _aiAgent!.AnalyzeAsync(context, latestSegment, aiCts.Token);
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

            if (current.Status != MeetingSessionStatus.Active)
            {
                state.MarkCompleted(analysisGeneration);
                await ClearAnalyzingFlagAsync(current, cancellationToken);
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
                aiDecision.RecommendedTasks,
                finalTranscript,
                current.Checklist,
                current.Purpose,
                mergeWarnings);
            var contextualCards = ApplyContextualCards(
                current.ContextualCards,
                aiDecision.ContextualCards,
                analysisWindow);

            state.MarkCompleted(analysisGeneration);
            var mergedSession = current with
            {
                Checklist = checklist,
                RecommendedTasks = recommendations,
                ContextualCards = contextualCards,
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
            var updated = await mutation(session);
            updated = updated with
            {
                IsAnalyzing = updated.Status == MeetingSessionStatus.Active
                    && IsAnalysisPending(sessionId),
                Revision = session.Revision + 1,
                UpdatedAtUtc = DateTimeOffset.UtcNow
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
        IReadOnlyList<RecommendedTaskProposal> proposals,
        IReadOnlyList<TranscriptSegment> transcript,
        IReadOnlyList<ChecklistItemState> checklist,
        MeetingPurpose purpose,
        ICollection<string> warnings)
    {
        var segmentIds = transcript.Select(segment => segment.Id).ToHashSet();
        var knownTitles = current
            .Select(task => HeuristicConversationCoachAgent.Normalize(task.Title))
            .ToHashSet(StringComparer.Ordinal);
        var coveredContext = checklist
            .SelectMany(item => new[] { item.Title, item.CompletionCriteria })
            .Concat(purpose.SuccessCriteria)
            .Select(HeuristicConversationCoachAgent.Normalize)
            .Where(value => value.Length > 0)
            .ToArray();
        var result = current.ToList();

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
                Evidence: []));
        }

        return result;
    }

    private static IReadOnlyList<ContextualCardState> ApplyContextualCards(
        IReadOnlyList<ContextualCardState> current,
        IReadOnlyList<ContextualCardProposal> proposals,
        IReadOnlyList<TranscriptSegment> analysisWindow)
    {
        var analysisWindowById = analysisWindow.ToDictionary(segment => segment.Id);
        var knownTitles = current
            .Select(card => HeuristicConversationCoachAgent.Normalize(card.Title))
            .ToHashSet(StringComparer.Ordinal);
        var result = current.TakeLast(MaximumRetainedContextualCards).ToList();
        var acceptedCount = 0;

        foreach (var proposal in proposals)
        {
            if (acceptedCount >= MaximumContextualCardsPerAnalysis)
            {
                break;
            }

            if (proposal is null
                || !Enum.IsDefined(proposal.Kind)
                || string.IsNullOrWhiteSpace(proposal.Title)
                || proposal.Title.Trim().Length > 80
                || string.IsNullOrWhiteSpace(proposal.Content)
                || proposal.Content.Trim().Length > 320
                || !double.IsFinite(proposal.Confidence)
                || proposal.Confidence is < ContextualCardThreshold or > 1
                || proposal.SourceTranscriptSegmentIds is not { Count: > 0 }
                || proposal.SourceTranscriptSegmentIds.Any(
                    id => !analysisWindowById.ContainsKey(id))
                || !PresentationCoachingPolicy.IsTitleGrounded(
                    proposal.Title,
                    proposal.SourceTranscriptSegmentIds,
                    analysisWindow))
            {
                continue;
            }

            var normalizedTitle = HeuristicConversationCoachAgent.Normalize(proposal.Title);
            if (normalizedTitle.Length == 0 || !knownTitles.Add(normalizedTitle))
            {
                continue;
            }

            result.Add(new ContextualCardState(
                Guid.NewGuid(),
                proposal.Kind,
                proposal.Title.Trim(),
                proposal.Content.Trim(),
                proposal.Confidence,
                proposal.SourceTranscriptSegmentIds.Distinct().ToArray(),
                DateTimeOffset.UtcNow));
            acceptedCount++;
        }

        return result.TakeLast(MaximumRetainedContextualCards).ToArray();
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

    private static void EnsureActive(MeetingSessionState session)
    {
        if (session.Status != MeetingSessionStatus.Active)
        {
            throw new InvalidOperationException("The meeting session is already completed.");
        }
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

        public Task? BackgroundTask;

        public ChannelReader<long> TriggerReader => _trigger.Reader;

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

        public void Dispose() => _trigger.Writer.TryComplete();
    }
}
