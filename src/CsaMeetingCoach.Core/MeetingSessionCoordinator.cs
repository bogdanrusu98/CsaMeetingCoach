using System.Collections.Concurrent;
using CsaMeetingCoach.Contracts;

namespace CsaMeetingCoach.Core;

public sealed class MeetingSessionCoordinator(
    IMeetingSessionStore store,
    IMeetingChecklistPlanner checklistPlanner,
    IConversationCoachAgent coachAgent,
    ISessionUpdatePublisher updatePublisher)
{
    public const double AutoCompletionThreshold = 0.82;

    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _sessionLocks = new();

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
            checklistPlanner.CreateChecklist(request.Purpose, request.Checklist),
            Transcript: [],
            RecommendedTasks: [],
            Warnings: [],
            TeamsOnlineMeetingId: NormalizeMeetingId(request.TeamsOnlineMeetingId));

        await store.SaveAsync(session, cancellationToken);
        await updatePublisher.PublishAsync(session, cancellationToken);
        return session;
    }

    public async Task<MeetingSessionState?> GetAsync(
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        return await store.GetAsync(sessionId, cancellationToken);
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
            var session = await store.GetAsync(sessionId, cancellationToken)
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
                await store.SaveAsync(transcriptUpdate, cancellationToken);
                await updatePublisher.PublishAsync(transcriptUpdate, cancellationToken);
            }

            if (!segment.IsFinal)
            {
                return transcriptUpdate;
            }

            var finalTranscript = transcript
                .Where(transcriptSegment => transcriptSegment.IsFinal)
                .ToArray();
            var context = new CoachAgentContext(
                transcriptUpdate.Purpose,
                transcriptUpdate.Checklist,
                finalTranscript.TakeLast(20).ToArray(),
                transcriptUpdate.RecommendedTasks);
            var decision = await coachAgent.AnalyzeAsync(context, segment, cancellationToken);

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
                transcriptUpdate.Checklist,
                transcriptUpdate.Purpose,
                warnings);
            var analyzedAtUtc = DateTimeOffset.UtcNow;
            var analyzedTranscript = transcript
                .Select(item => item.Id == segment.Id
                    ? item with { AnalyzedAtUtc = analyzedAtUtc }
                    : item)
                .ToArray();

            var coachingUpdate = transcriptUpdate with
            {
                Transcript = analyzedTranscript,
                Checklist = checklist,
                RecommendedTasks = recommendations,
                Warnings = warnings.TakeLast(20).ToArray(),
                Revision = transcriptUpdate.Revision + 1,
                UpdatedAtUtc = analyzedAtUtc
            };

            await store.SaveAsync(coachingUpdate, cancellationToken);
            await updatePublisher.PublishAsync(coachingUpdate, cancellationToken);
            return coachingUpdate;
        }
        finally
        {
            gate.Release();
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
            var session = await store.GetAsync(sessionId, cancellationToken)
                ?? throw new KeyNotFoundException($"Meeting session {sessionId} was not found.");
            var updated = await mutation(session);
            updated = updated with
            {
                Revision = session.Revision + 1,
                UpdatedAtUtc = DateTimeOffset.UtcNow
            };

            await store.SaveAsync(updated, cancellationToken);
            await updatePublisher.PublishAsync(updated, cancellationToken);
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
        var byItem = evaluations
            .Where(evaluation => evaluation.ShouldComplete
                && evaluation.Confidence >= AutoCompletionThreshold)
            .GroupBy(evaluation => evaluation.ChecklistItemId)
            .ToDictionary(group => group.Key, group => group.OrderByDescending(item => item.Confidence).First());

        return current.Select(item =>
        {
            if (item.Status == ChecklistItemStatus.Completed
                || !byItem.TryGetValue(item.Id, out var evaluation))
            {
                return item;
            }

            if (string.IsNullOrWhiteSpace(evaluation.EvidenceQuote)
                || !latestSegment.Text.Contains(
                    evaluation.EvidenceQuote,
                    StringComparison.Ordinal))
            {
                warnings.Add(
                    $"Rejected completion for '{item.Title}': agent evidence was not present in the latest transcript segment.");
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

        foreach (var proposal in proposals.Where(item => item.Confidence >= 0.70))
        {
            if (string.IsNullOrWhiteSpace(proposal.Title))
            {
                warnings.Add("Rejected a recommended task because the agent returned an empty title.");
                continue;
            }

            if (proposal.SourceTranscriptSegmentIds.Count == 0
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
        foreach (var unknown in evaluations.Where(item => !knownIds.Contains(item.RecommendationId)))
        {
            warnings.Add(
                $"Rejected recommendation completion for unknown recommendation {unknown.RecommendationId}.");
        }
        foreach (var invalid in evaluations.Where(item => item.ShouldComplete
            && (!double.IsFinite(item.Confidence)
                || item.Confidence is < 0 or > 1)))
        {
            warnings.Add(
                $"Rejected recommendation completion {invalid.RecommendationId}: confidence was invalid.");
        }

        var byItem = evaluations
            .Where(evaluation => knownIds.Contains(evaluation.RecommendationId)
                && evaluation.ShouldComplete
                && double.IsFinite(evaluation.Confidence)
                && evaluation.Confidence is >= AutoCompletionThreshold and <= 1)
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

            if (string.IsNullOrWhiteSpace(evaluation.EvidenceQuote)
                || !latestSegment.Text.Contains(
                    evaluation.EvidenceQuote,
                    StringComparison.Ordinal))
            {
                warnings.Add(
                    $"Rejected completion for '{item.Title}': agent evidence was not present in the latest transcript segment.");
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
}
