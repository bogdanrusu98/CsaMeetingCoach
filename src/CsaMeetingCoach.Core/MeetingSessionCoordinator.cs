using System.Collections.Concurrent;
using System.Threading.Channels;
using CsaMeetingCoach.Contracts;
using Microsoft.Extensions.Logging;

namespace CsaMeetingCoach.Core;

public sealed class MeetingSessionCoordinator : IDisposable
{
    public const double AutoCompletionThreshold = 0.82;

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
            TeamsOnlineMeetingId: NormalizeMeetingId(request.TeamsOnlineMeetingId));

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
            var context = new CoachAgentContext(
                transcriptUpdate.Purpose,
                transcriptUpdate.Checklist,
                finalTranscript.TakeLast(20).ToArray(),
                transcriptUpdate.RecommendedTasks);
            var deterministicDecision = await _deterministicAgent.AnalyzeAsync(
                context, segment, cancellationToken);
            var fastLaneDecision = _aiAgent is null
                ? deterministicDecision
                : deterministicDecision with { RecommendedTasks = [] };
            var decision = CrossCuttingRecommendationPolicy.Apply(fastLaneDecision, segment);

            var warnings = transcriptUpdate.Warnings.ToList();
            var checklist = ApplyChecklistEvaluations(
                transcriptUpdate.Checklist,
                decision.ChecklistEvaluations,
                segment,
                warnings);
            var evaluatedRecommendations = ApplyRecommendationEvaluations(
                transcriptUpdate.RecommendedTasks,
                decision.RecommendationEvaluations ?? [],
                segment,
                warnings);
            var recommendations = ApplyRecommendations(
                evaluatedRecommendations,
                decision.RecommendedTasks,
                finalTranscript,
                segment,
                transcriptUpdate.Checklist,
                transcriptUpdate.Purpose,
                warnings);
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
                Warnings = warnings.TakeLast(20).ToArray(),
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
                // Debounce: hold off until the window expires, resetting on each new trigger.
                var deadline = DateTimeOffset.UtcNow + _analysisOptions.DebounceWindow;
                while (true)
                {
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
                            deadline = DateTimeOffset.UtcNow + _analysisOptions.DebounceWindow;
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
                    await TryAddAnalysisWarningAsync(
                        sessionId,
                        "AI coaching analysis encountered an error and was skipped. Transcript was preserved.",
                        state.IsAnalyzing);
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
                context = new CoachAgentContext(
                    session.Purpose,
                    session.Checklist,
                    finalTranscript,
                    session.RecommendedTasks);
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
            await TryAddAnalysisWarningAsync(
                sessionId,
                "AI coaching analysis timed out. Transcript was preserved.",
                state.IsAnalyzing);
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
            var currentLatestSegment = finalTranscript.LastOrDefault();
            if (state.LatestRequestedGeneration > analysisGeneration
                || currentLatestSegment?.Id != latestSegment.Id)
            {
                state.MarkCompleted(analysisGeneration);
                return;
            }

            var mergeWarnings = current.Warnings.ToList();

            // Use the originally analyzed segment for evidence checking (not the current latest).
            // This preserves evidence-quote integrity for AI completions.
            var checklist = ApplyChecklistEvaluations(
                current.Checklist,
                aiDecision.ChecklistEvaluations,
                latestSegment,
                mergeWarnings);
            var evaluatedRecommendations = ApplyRecommendationEvaluations(
                current.RecommendedTasks,
                aiDecision.RecommendationEvaluations ?? [],
                latestSegment,
                mergeWarnings);
            var recommendations = ApplyRecommendations(
                evaluatedRecommendations,
                aiDecision.RecommendedTasks,
                finalTranscript,
                latestSegment,
                current.Checklist,
                current.Purpose,
                mergeWarnings);

            state.MarkCompleted(analysisGeneration);
            var mergedSession = current with
            {
                Checklist = checklist,
                RecommendedTasks = recommendations,
                Warnings = mergeWarnings.TakeLast(20).ToArray(),
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
        bool isAnalyzing)
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

                var updated = session with
                {
                    Warnings = session.Warnings.Append(warning).TakeLast(20).ToArray(),
                    IsAnalyzing = isAnalyzing,
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
                    Evidence = []
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
                    Evidence = []
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
        TranscriptSegment latestSegment,
        ICollection<string> warnings)
    {
        var knownIds = current.Select(item => item.Id).ToHashSet();
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
                && !latestSegment.Text.Contains(
                    evaluation.EvidenceQuote,
                    StringComparison.Ordinal))
            {
                warnings.Add(
                    $"Rejected checklist completion for item {evaluation.ChecklistItemId}: agent evidence was not present in the latest transcript segment.");
            }
        }

        var byItem = evaluations
            .Where(evaluation => evaluation is not null
                && knownIds.Contains(evaluation.ChecklistItemId)
                && evaluation.ShouldComplete
                && double.IsFinite(evaluation.Confidence)
                && evaluation.Confidence is >= AutoCompletionThreshold and <= 1
                && !string.IsNullOrWhiteSpace(evaluation.Reason)
                && !string.IsNullOrWhiteSpace(evaluation.EvidenceQuote)
                && latestSegment.Text.Contains(
                    evaluation.EvidenceQuote,
                    StringComparison.Ordinal))
            .GroupBy(evaluation => evaluation.ChecklistItemId)
            .ToDictionary(group => group.Key, group => group.OrderByDescending(item => item.Confidence).First());

        return current.Select(item =>
        {
            if (item.Status == ChecklistItemStatus.Completed
                || !byItem.TryGetValue(item.Id, out var evaluation))
            {
                return item;
            }

            var evidence = new ChecklistEvidence(
                latestSegment.Id,
                latestSegment.Speaker,
                evaluation.EvidenceQuote.Trim(),
                latestSegment.OccurredAtUtc,
                Math.Clamp(evaluation.Confidence, 0, 1));

            return item with
            {
                Status = ChecklistItemStatus.Completed,
                AutoCompleted = true,
                Confidence = evidence.Confidence,
                CompletionReason = evaluation.Reason.Trim(),
                CompletedAtUtc = DateTimeOffset.UtcNow,
                Evidence = item.Evidence.Append(evidence).ToArray()
            };
        }).ToArray();
    }

    private static IReadOnlyList<RecommendedTaskState> ApplyRecommendations(
        IReadOnlyList<RecommendedTaskState> current,
        IReadOnlyList<RecommendedTaskProposal> proposals,
        IReadOnlyList<TranscriptSegment> transcript,
        TranscriptSegment latestSegment,
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
                || !proposal.SourceTranscriptSegmentIds.Contains(latestSegment.Id)
                || proposal.SourceTranscriptSegmentIds.Any(id => !segmentIds.Contains(id)))
            {
                warnings.Add(
                    $"Rejected recommended task '{proposal.Title}': transcript evidence was missing.");
                continue;
            }

            var normalizedTitle = HeuristicConversationCoachAgent.Normalize(proposal.Title);
            if (!knownTitles.Add(normalizedTitle)
                || coveredContext.Any(context => context == normalizedTitle))
            {
                continue;
            }

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

    private static IReadOnlyList<RecommendedTaskState> ApplyRecommendationEvaluations(
        IReadOnlyList<RecommendedTaskState> current,
        IReadOnlyList<RecommendationEvaluation> evaluations,
        TranscriptSegment latestSegment,
        ICollection<string> warnings)
    {
        var knownIds = current.Select(item => item.Id).ToHashSet();
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
                && !latestSegment.Text.Contains(
                    evaluation.EvidenceQuote,
                    StringComparison.Ordinal))
            {
                warnings.Add(
                    $"Rejected recommendation completion {evaluation.RecommendationId}: agent evidence was not present in the latest transcript segment.");
            }
        }

        var byItem = evaluations
            .Where(evaluation => evaluation is not null
                && knownIds.Contains(evaluation.RecommendationId)
                && evaluation.ShouldComplete
                && double.IsFinite(evaluation.Confidence)
                && evaluation.Confidence is >= AutoCompletionThreshold and <= 1
                && !string.IsNullOrWhiteSpace(evaluation.Reason)
                && !string.IsNullOrWhiteSpace(evaluation.EvidenceQuote)
                && latestSegment.Text.Contains(
                    evaluation.EvidenceQuote,
                    StringComparison.Ordinal))
            .GroupBy(evaluation => evaluation.RecommendationId)
            .ToDictionary(
                group => group.Key,
                group => group.OrderByDescending(item => item.Confidence).First());

        return current.Select(item =>
        {
            if (item.Status != RecommendationStatus.Accepted
                || !byItem.TryGetValue(item.Id, out var evaluation))
            {
                return item;
            }

            if (item.AcceptedAtUtc is null
                || latestSegment.OccurredAtUtc <= item.AcceptedAtUtc
                || item.SourceTranscriptSegmentIds.Contains(latestSegment.Id))
            {
                warnings.Add(
                    $"Rejected completion for '{item.Title}': evidence did not occur after acceptance.");
                return item;
            }

            var evidence = new ChecklistEvidence(
                latestSegment.Id,
                latestSegment.Speaker,
                evaluation.EvidenceQuote,
                latestSegment.OccurredAtUtc,
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
