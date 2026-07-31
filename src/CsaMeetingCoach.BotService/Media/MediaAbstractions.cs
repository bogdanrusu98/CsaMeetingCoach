namespace CsaMeetingCoach.BotService.Media;

public sealed record JoinMeetingRequest(
    string TenantId,
    string ChatId,
    string OrganizerObjectId,
    Guid CoachSessionId,
    string TeamsOnlineMeetingId,
    string? MessageId = null,
    Guid? MediaSessionId = null);

public sealed record FinalTranscript(
    string RecognitionId,
    string Text,
    DateTimeOffset OccurredAtUtc);

public interface IAudioFrameSink
{
    bool TryWrite(byte[] pcmS16Le16KhzMono);
}

public interface IAudioReceiver : IDisposable
{
    void Start(IAudioFrameSink sink);
}

public interface ICallHandle : IAsyncDisposable
{
    string Id { get; }
    IAudioReceiver AudioReceiver { get; }
    Task AwaitEstablishedAsync(TimeSpan timeout, CancellationToken cancellationToken);
    Task UpdateRecordingStatusAsync(CancellationToken cancellationToken);
    Task KeepAliveAsync(CancellationToken cancellationToken);
    Task DeleteAsync(CancellationToken cancellationToken);
}

public interface IGraphCallClient : IAsyncDisposable
{
    bool Enabled { get; }
    bool Configured { get; }
    bool Ready { get; }
    event Func<string, Task>? CallEnded;
    Task<ICallHandle> JoinAsync(JoinMeetingRequest request, CancellationToken cancellationToken);
    Task<HttpResponseMessage> ProcessNotificationAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken);
}

public interface ISpeechSession : IAsyncDisposable
{
    event Action<FinalTranscript>? FinalRecognized;
    event Action<Exception>? Failed;
    Task StartAsync(CancellationToken cancellationToken);
    void Write(ReadOnlySpan<byte> pcmS16Le16KhzMono);
}

public interface ISpeechSessionFactory
{
    ISpeechSession Create();
}

public interface ICoachApiClient
{
    Task PublishAsync(
        Guid coachSessionId,
        string teamsOnlineMeetingId,
        Guid sourceSegmentId,
        FinalTranscript transcript,
        CancellationToken cancellationToken);
}

public sealed record PipelineSnapshot(
    int ActiveCalls,
    long FramesAccepted,
    long FramesDropped,
    long PreGateFramesDropped,
    long FinalSegmentsDropped,
    long FinalSegmentsPublished,
    long PublishFailures,
    long PipelineFailures,
    long KeepAliveFailures);
