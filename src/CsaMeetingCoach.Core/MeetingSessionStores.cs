using System.Collections.Concurrent;
using System.Text.Json;
using CsaMeetingCoach.Contracts;

namespace CsaMeetingCoach.Core;

public sealed class InMemoryMeetingSessionStore : IMeetingSessionStore
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
        _sessions[session.Id] = session;
        return Task.CompletedTask;
    }
}

public sealed class JsonMeetingSessionStore(string dataDirectory) : IMeetingSessionStore
{
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
            return session is null
                ? null
                : session with
                {
                    RecommendedTasks = session.RecommendedTasks
                        .Select(item => item with
                        {
                            AcceptedAtUtc = item.Status == RecommendationStatus.Accepted
                                && item.AcceptedAtUtc is null
                                    ? session.UpdatedAtUtc
                                    : item.AcceptedAtUtc,
                            Evidence = item.Evidence ?? []
                        })
                        .ToArray()
                };
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task SaveAsync(MeetingSessionState session, CancellationToken cancellationToken)
    {
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

    private string GetPath(Guid sessionId)
    {
        return Path.Combine(dataDirectory, $"{sessionId:N}.json");
    }
}
