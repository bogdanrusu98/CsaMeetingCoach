using System.Collections.Concurrent;
using System.Text.Json;
using CsaMeetingCoach.Contracts;

namespace CsaMeetingCoach.Core;

public sealed class InMemoryMeetingSessionStore : IExpiringMeetingSessionStore
{
    private readonly ConcurrentDictionary<Guid, MeetingSessionState> _sessions = new();

    public Task<MeetingSessionState?> GetAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _sessions.TryGetValue(sessionId, out var session);
        return Task.FromResult(session);
    }

    public Task SaveAsync(MeetingSessionState session, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (SessionLifecycle.IsExpired(session, DateTimeOffset.UtcNow))
        {
            throw new SessionExpiredException(session.Id);
        }

        _sessions[session.Id] = session;
        return Task.CompletedTask;
    }

    public async IAsyncEnumerable<MeetingSessionState> ListAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation]
        CancellationToken cancellationToken)
    {
        foreach (var session in _sessions.Values)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return session;
            await Task.Yield();
        }
    }

    public Task DeleteAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _sessions.TryRemove(sessionId, out _);
        return Task.CompletedTask;
    }
}

public sealed class JsonMeetingSessionStore(string dataDirectory) : IExpiringMeetingSessionStore
{
    private const int CompletionEligibilityMigrationVersion = 3;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _locks = new();

    public async Task<MeetingSessionState?> GetAsync(
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        var path = GetPath(sessionId);
        var gate = _locks.GetOrAdd(sessionId, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            await using var stream = File.OpenRead(path);
            var session = await JsonSerializer.DeserializeAsync<MeetingSessionState>(
                stream,
                JsonOptions,
                cancellationToken);
            var requiresCompletionEligibilityMigration = session is not null
                && session.StateSchemaVersion < CompletionEligibilityMigrationVersion;
            var finalTranscriptCount = session?.Transcript.Count(segment => segment.IsFinal) ?? 0;
            var expiresAtUtc = session is null
                ? default
                : session.ExpiresAtUtc == default
                    ? session.CreatedAtUtc + SessionLifecycle.Lifetime
                    : session.ExpiresAtUtc;
            return session is null
                ? null
                : session with
                {
                    IsAnalyzing = false,
                    StateSchemaVersion = MeetingSessionState.CurrentSchemaVersion,
                    ExpiresAtUtc = expiresAtUtc,
                    Participants = session.Participants ?? [],
                    KnowledgeSources = session.KnowledgeSources ?? [],
                    ShownDefinitionKeys = session.ShownDefinitionKeys ?? new(StringComparer.Ordinal),
                    ShownHintKeys = session.ShownHintKeys ?? new(StringComparer.Ordinal),
                    DefinitionCooldowns = session.DefinitionCooldowns ?? new(StringComparer.Ordinal),
                    HintCooldowns = session.HintCooldowns ?? new(StringComparer.Ordinal),
                    ContentFingerprints = session.ContentFingerprints ?? new(StringComparer.Ordinal),
                    Checklist = session.Checklist
                        .Select(item => item with
                        {
                            CompletionEligibleFromTranscriptIndex =
                                requiresCompletionEligibilityMigration
                                    && item.Status == ChecklistItemStatus.Pending
                                    && item.CompletionEligibleFromTranscriptIndex is null
                                        ? finalTranscriptCount
                                        : item.CompletionEligibleFromTranscriptIndex
                        })
                        .ToArray(),
                    RecommendedTasks = RecommendationIntentPolicy.Consolidate(
                        session.Template,
                        session.Checklist,
                        session.RecommendedTasks
                            .Select(item => item with
                            {
                                IntentKey = RecommendationIntentPolicy.Resolve(
                                    session.Template,
                                    item),
                                AcceptedAtUtc =
                                    item.Status == RecommendationStatus.Accepted
                                    && item.AcceptedAtUtc is null
                                        ? session.UpdatedAtUtc
                                        : item.AcceptedAtUtc,
                                Evidence = item.Evidence ?? [],
                                KnowledgeSourceIds = item.KnowledgeSourceIds ?? [],
                                WordingSourceTranscriptSegmentIds =
                                    ResolveWordingSources(item),
                                CompletionEligibleFromTranscriptIndex =
                                    item.Status == RecommendationStatus.Accepted
                                    && item.CompletionEligibleFromTranscriptIndex is null
                                        ? finalTranscriptCount
                                        : item.CompletionEligibleFromTranscriptIndex
                            })
                            .ToArray()),
                    ContextualCards = (session.ContextualCards ?? [])
                        .Select(card => card with
                        {
                            KnowledgeSourceIds = card.KnowledgeSourceIds ?? []
                        })
                        .ToArray()
                };
        }
        finally
        {
            gate.Release();
        }
    }

    public async IAsyncEnumerable<MeetingSessionState> ListAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation]
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(dataDirectory))
        {
            yield break;
        }

        var seenSessionIds = new HashSet<Guid>();
        foreach (var path in Directory.EnumerateFiles(dataDirectory, "*.json"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Guid.TryParseExact(Path.GetFileNameWithoutExtension(path), "N", out var sessionId))
            {
                continue;
            }

            var session = await GetAsync(sessionId, cancellationToken);
            if (session is not null)
            {
                seenSessionIds.Add(sessionId);
                yield return session;
            }
        }

        foreach (var temporaryPath in Directory.EnumerateFiles(
            dataDirectory,
            "*.json.*.tmp"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var fileName = Path.GetFileName(temporaryPath);
            if (fileName.Length < 32
                || !Guid.TryParseExact(fileName[..32], "N", out var sessionId)
                || seenSessionIds.Contains(sessionId))
            {
                continue;
            }

            MeetingSessionState? session;
            try
            {
                await using var stream = File.OpenRead(temporaryPath);
                session = await JsonSerializer.DeserializeAsync<MeetingSessionState>(
                    stream,
                    JsonOptions,
                    cancellationToken);
            }
            catch (IOException)
            {
                continue;
            }
            catch (JsonException)
            {
                if (File.GetLastWriteTimeUtc(temporaryPath)
                    <= DateTime.UtcNow - SessionLifecycle.Lifetime)
                {
                    File.Delete(temporaryPath);
                }

                continue;
            }

            if (session is null || session.Id != sessionId)
            {
                continue;
            }

            seenSessionIds.Add(sessionId);
            yield return session.ExpiresAtUtc == default
                ? session with
                {
                    ExpiresAtUtc = session.CreatedAtUtc + SessionLifecycle.Lifetime
                }
                : session;
        }
    }

    public async Task DeleteAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        var path = GetPath(sessionId);
        var gate = _locks.GetOrAdd(sessionId, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (Directory.Exists(dataDirectory))
            {
                foreach (var temporaryPath in Directory.EnumerateFiles(
                    dataDirectory,
                    $"{sessionId:N}.json.*.tmp"))
                {
                    File.Delete(temporaryPath);
                }
            }

            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task SaveAsync(MeetingSessionState session, CancellationToken cancellationToken)
    {
        if (SessionLifecycle.IsExpired(session, DateTimeOffset.UtcNow))
        {
            throw new SessionExpiredException(session.Id);
        }

        Directory.CreateDirectory(dataDirectory);
        var path = GetPath(session.Id);
        var temporaryPath = string.Concat(path, ".", Guid.NewGuid().ToString("N"), ".tmp");
        var gate = _locks.GetOrAdd(session.Id, static _ => new SemaphoreSlim(1, 1));

        await gate.WaitAsync(cancellationToken);
        try
        {
            await using (var stream = File.Create(temporaryPath))
            {
                await JsonSerializer.SerializeAsync(
                    stream,
                    session,
                    JsonOptions,
                    cancellationToken);
            }

            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }

            gate.Release();
        }
    }

    private static IReadOnlyList<Guid> ResolveWordingSources(
        RecommendedTaskState recommendation)
    {
        var sourceIds = (recommendation.SourceTranscriptSegmentIds ?? [])
            .Distinct()
            .TakeLast(20)
            .ToArray();
        var sourceIdSet = sourceIds.ToHashSet();
        var persisted = (recommendation.WordingSourceTranscriptSegmentIds ?? [])
            .Where(sourceIdSet.Contains)
            .Distinct()
            .TakeLast(20)
            .ToArray();
        return persisted.Length > 0
            ? persisted
            : sourceIds;
    }

    private string GetPath(Guid sessionId)
    {
        return Path.Combine(dataDirectory, $"{sessionId:N}.json");
    }
}
