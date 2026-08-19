using CsaMeetingCoach.Contracts;
using CsaMeetingCoach.Core;

namespace CsaMeetingCoach.Api;

public sealed class SessionKnowledgeService(
    MeetingSessionCoordinator coordinator,
    ISessionKnowledgeStore knowledgeStore,
    IKnowledgeLinkFetcher linkFetcher,
    TimeProvider timeProvider)
{
    public async Task<MeetingSessionState> AddFileAsync(
        Guid sessionId,
        IFormFile file,
        KnowledgeSourceVisibility visibility,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(file);
        ValidateVisibility(visibility);
        if (file.Length <= 0 || file.Length > SessionKnowledgeLimits.MaximumFileBytes)
        {
            throw new ArgumentException(
                "Knowledge files must contain data and cannot exceed 50 MB.");
        }

        await EnsureCapacityAsync(sessionId, file.Length, cancellationToken);
        var sourceId = Guid.NewGuid();
        StoredKnowledgeContent stored;
        await using (var stream = file.OpenReadStream())
        {
            stored = await knowledgeStore.StoreFileAsync(
                sessionId,
                sourceId,
                NormalizeFileDisplayName(file.FileName),
                visibility,
                file.FileName,
                file.ContentType,
                stream,
                cancellationToken);
        }

        try
        {
            return await coordinator.AddKnowledgeSourceAsync(
                sessionId,
                new KnowledgeSourceState(
                    sourceId,
                    KnowledgeSourceKind.File,
                    NormalizeFileDisplayName(file.FileName),
                    visibility,
                    KnowledgeSourceStatus.Ready,
                    timeProvider.GetUtcNow(),
                    stored.SizeBytes,
                    stored.MediaType),
                cancellationToken);
        }
        catch
        {
            if (!await IsSourcePersistedAsync(sessionId, sourceId))
            {
                await DeleteStoredSourceAsync(sessionId, sourceId);
            }

            throw;
        }
    }

    public async Task<MeetingSessionState> AddLinkAsync(
        Guid sessionId,
        AddKnowledgeLinkRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateVisibility(request.Visibility);
        await EnsureCapacityAsync(sessionId, 0, cancellationToken);
        var fetched = await linkFetcher.FetchAsync(
            request.Url,
            request.DisplayName,
            cancellationToken);
        var sourceId = Guid.NewGuid();
        await knowledgeStore.StoreLinkAsync(
            sessionId,
            sourceId,
            fetched.DisplayName,
            request.Visibility,
            fetched.Content,
            cancellationToken);
        try
        {
            return await coordinator.AddKnowledgeSourceAsync(
                sessionId,
                new KnowledgeSourceState(
                    sourceId,
                    KnowledgeSourceKind.Link,
                    fetched.DisplayName,
                    request.Visibility,
                    KnowledgeSourceStatus.Ready,
                    timeProvider.GetUtcNow(),
                    fetched.SizeBytes,
                    fetched.MediaType,
                    fetched.SafeSourceUri),
                cancellationToken);
        }
        catch
        {
            if (!await IsSourcePersistedAsync(sessionId, sourceId))
            {
                await DeleteStoredSourceAsync(sessionId, sourceId);
            }

            throw;
        }
    }

    public async Task<MeetingSessionState> DeleteAsync(
        Guid sessionId,
        Guid sourceId,
        CancellationToken cancellationToken)
    {
        var before = await coordinator.GetAsync(sessionId, cancellationToken)
            ?? throw new KeyNotFoundException($"Meeting session {sessionId} was not found.");
        if (!before.KnowledgeSources.Any(source => source.Id == sourceId))
        {
            await DeleteStoredSourceAsync(sessionId, sourceId);
            return before;
        }

        MeetingSessionState updated;
        try
        {
            updated = await coordinator.RemoveKnowledgeSourceAsync(
                sessionId,
                sourceId,
                cancellationToken);
        }
        catch (SessionUpdateNotificationException)
        {
            await DeleteStoredSourceAsync(sessionId, sourceId);
            throw;
        }

        await DeleteStoredSourceAsync(sessionId, sourceId);
        return updated;
    }

    private async Task<bool> IsSourcePersistedAsync(Guid sessionId, Guid sourceId)
    {
        var session = await coordinator.GetAsync(sessionId, CancellationToken.None);
        return session?.KnowledgeSources.Any(source => source.Id == sourceId) == true;
    }

    private async Task DeleteStoredSourceAsync(Guid sessionId, Guid sourceId)
    {
        using var cleanupTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await knowledgeStore.DeleteSourceAsync(
            sessionId,
            sourceId,
            cleanupTimeout.Token);
    }

    private async Task EnsureCapacityAsync(
        Guid sessionId,
        long additionalBytes,
        CancellationToken cancellationToken)
    {
        var session = await coordinator.GetAsync(sessionId, cancellationToken)
            ?? throw new KeyNotFoundException($"Meeting session {sessionId} was not found.");
        if (SessionLifecycle.IsExpired(session, timeProvider.GetUtcNow()))
        {
            throw new SessionExpiredException(sessionId);
        }

        if (session.Status != MeetingSessionStatus.Active)
        {
            throw new InvalidOperationException(
                "Knowledge can only be added to an active session.");
        }

        if (session.KnowledgeSources.Count >= SessionKnowledgeLimits.MaximumSourceCount)
        {
            throw new InvalidOperationException(
                $"A session can contain at most {SessionKnowledgeLimits.MaximumSourceCount} knowledge sources.");
        }

        var totalBytes = checked(
            session.KnowledgeSources.Sum(source => source.SizeBytes) + additionalBytes);
        if (totalBytes > SessionKnowledgeLimits.MaximumTotalBytes)
        {
            throw new InvalidOperationException(
                "Session knowledge exceeds the 100 MB aggregate limit.");
        }
    }

    private static void ValidateVisibility(KnowledgeSourceVisibility visibility)
    {
        if (!Enum.IsDefined(visibility))
        {
            throw new ArgumentException("Knowledge visibility is invalid.");
        }
    }

    private static string NormalizeFileDisplayName(string fileName)
    {
        var name = Path.GetFileName(fileName);
        if (string.IsNullOrWhiteSpace(name)
            || name.Length > 255
            || name.Any(char.IsControl))
        {
            throw new ArgumentException("Knowledge file name is invalid.", nameof(fileName));
        }

        return name;
    }
}
