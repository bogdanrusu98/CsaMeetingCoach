using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Channels;
using CsaMeetingCoach.BotService.Configuration;
using Microsoft.Extensions.Options;

namespace CsaMeetingCoach.BotService.Media;

public sealed class AudioPipeline : IAudioFrameSink, IAsyncDisposable
{
    private static readonly TimeSpan PublishShutdownTimeout = TimeSpan.FromSeconds(2);
    private readonly Channel<byte[]> frames;
    private readonly Channel<QueuedTranscript> transcripts;
    private readonly ISpeechSession speech;
    private readonly ICoachApiClient coach;
    private readonly Guid coachSessionId;
    private readonly string meetingId;
    private readonly PipelineMetrics metrics;
    private readonly ILogger<AudioPipeline> logger;
    private readonly CancellationTokenSource stopping = new();
    private Task? framePump;
    private Task? publishPump;
    private int gateOpen;

    public AudioPipeline(
        int frameCapacity,
        int transcriptCapacity,
        ISpeechSession speech,
        ICoachApiClient coach,
        Guid coachSessionId,
        string meetingId,
        PipelineMetrics metrics,
        ILogger<AudioPipeline> logger)
    {
        frames = CreateChannel<byte[]>(frameCapacity);
        transcripts = CreateChannel<QueuedTranscript>(transcriptCapacity);
        this.speech = speech;
        this.coach = coach;
        this.coachSessionId = coachSessionId;
        this.meetingId = meetingId;
        this.metrics = metrics;
        this.logger = logger;
        speech.FinalRecognized += OnFinalRecognized;
        speech.Failed += OnSpeechFailed;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await speech.StartAsync(cancellationToken).ConfigureAwait(false);
        framePump = SuperviseAsync(PumpFramesAsync, "audio frame");
        publishPump = SuperviseAsync(PublishTranscriptsAsync, "transcript publish");
        Volatile.Write(ref gateOpen, 1);
    }

    public bool TryWrite(byte[] pcmS16Le16KhzMono)
    {
        if (Volatile.Read(ref gateOpen) == 0)
        {
            metrics.PreGateFrameDropped();
            logger.LogWarning("Discarded an audio frame while the recording-status gate was closed.");
            return false;
        }

        if (frames.Writer.TryWrite(pcmS16Le16KhzMono))
        {
            metrics.FrameAccepted();
            return true;
        }

        metrics.FrameDropped();
        logger.LogWarning("Dropped an audio frame because the bounded audio queue is full or closed.");
        return false;
    }

    public async ValueTask DisposeAsync()
    {
        Volatile.Write(ref gateOpen, 0);
        frames.Writer.TryComplete();
        var failures = new List<Exception>();
        try
        {
            if (framePump is not null)
            {
                await TryShutdownAsync(framePump, failures).ConfigureAwait(false);
            }

            try
            {
                await speech.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }

            speech.FinalRecognized -= OnFinalRecognized;
            speech.Failed -= OnSpeechFailed;
            transcripts.Writer.TryComplete();
            if (publishPump is not null)
            {
                var completed = await Task.WhenAny(
                    publishPump,
                    Task.Delay(PublishShutdownTimeout)).ConfigureAwait(false);
                if (completed != publishPump)
                {
                    logger.LogWarning(
                        "Transcript publishing did not drain within {Timeout}; canceling shutdown.",
                        PublishShutdownTimeout);
                    stopping.Cancel();
                }

                await TryShutdownAsync(publishPump, failures).ConfigureAwait(false);
            }

            var abandoned = 0;
            while (transcripts.Reader.TryRead(out _))
            {
                metrics.FinalSegmentDropped();
                abandoned++;
            }

            if (abandoned > 0)
            {
                logger.LogWarning(
                    "Discarded {Count} queued final transcripts during pipeline shutdown.",
                    abandoned);
            }
        }
        finally
        {
            stopping.Cancel();
            stopping.Dispose();
        }

        if (failures.Count > 0)
        {
            throw new AggregateException("Audio pipeline shutdown failed.", failures);
        }
    }

    private static async Task TryShutdownAsync(Task worker, ICollection<Exception> failures)
    {
        try
        {
            await worker.ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            failures.Add(exception);
        }
    }

    private static Channel<T> CreateChannel<T>(int capacity) =>
        Channel.CreateBounded<T>(new BoundedChannelOptions(capacity)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait,
            AllowSynchronousContinuations = false
        });

    private async Task SuperviseAsync(Func<CancellationToken, Task> worker, string workerName)
    {
        try
        {
            await worker(stopping.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stopping.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            metrics.PipelineFailed();
            Volatile.Write(ref gateOpen, 0);
            stopping.Cancel();
            logger.LogError(
                exception,
                "The {WorkerName} pipeline worker failed; the recording-status gate is now closed.",
                workerName);
        }
    }

    private async Task PumpFramesAsync(CancellationToken cancellationToken)
    {
        await foreach (var frame in frames.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            speech.Write(frame);
        }
    }

    private async Task PublishTranscriptsAsync(CancellationToken cancellationToken)
    {
        await foreach (var queued in transcripts.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await coach.PublishAsync(
                    coachSessionId,
                    meetingId,
                    queued.SourceSegmentId,
                    queued.Transcript,
                    cancellationToken).ConfigureAwait(false);
                metrics.SegmentPublished();
            }
            catch (OperationCanceledException) when (stopping.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                metrics.PublishFailed();
                logger.LogError(
                    exception,
                    "Failed to publish transcript segment {SourceSegmentId}; closing the audio pipeline to prevent transcript gaps.",
                    queued.SourceSegmentId);
                throw;
            }
        }
    }

    private void OnSpeechFailed(Exception exception)
    {
        metrics.PipelineFailed();
        logger.LogError(exception, "Azure Speech stopped; closing the audio pipeline.");
        Volatile.Write(ref gateOpen, 0);
        stopping.Cancel();
    }

    private void OnFinalRecognized(FinalTranscript transcript)
    {
        if (string.IsNullOrWhiteSpace(transcript.Text))
        {
            return;
        }

        var queued = new QueuedTranscript(
            StableSegmentId(coachSessionId, transcript.RecognitionId),
            transcript);
        if (!transcripts.Writer.TryWrite(queued))
        {
            metrics.FinalSegmentDropped();
            logger.LogWarning(
                "Dropped final transcript {SourceSegmentId} because the bounded publish queue is full or closed.",
                queued.SourceSegmentId);
        }
    }

    internal static Guid StableSegmentId(Guid sessionId, string recognitionId)
    {
        var input = Encoding.UTF8.GetBytes($"{sessionId:N}:{recognitionId}");
        var hash = SHA256.HashData(input);
        return new Guid(hash.AsSpan(0, 16));
    }

    private sealed record QueuedTranscript(Guid SourceSegmentId, FinalTranscript Transcript);
}

public sealed class PipelineMetrics
{
    private long activeCalls;
    private long accepted;
    private long dropped;
    private long preGateDropped;
    private long finalSegmentsDropped;
    private long published;
    private long publishFailures;
    private long pipelineFailures;
    private long keepAliveFailures;

    public IDisposable TrackCall()
    {
        Interlocked.Increment(ref activeCalls);
        return new CallCounter(this);
    }

    public PipelineSnapshot Snapshot() => new(
        checked((int)Interlocked.Read(ref activeCalls)),
        Interlocked.Read(ref accepted),
        Interlocked.Read(ref dropped),
        Interlocked.Read(ref preGateDropped),
        Interlocked.Read(ref finalSegmentsDropped),
        Interlocked.Read(ref published),
        Interlocked.Read(ref publishFailures),
        Interlocked.Read(ref pipelineFailures),
        Interlocked.Read(ref keepAliveFailures));

    internal void FrameAccepted() => Interlocked.Increment(ref accepted);
    internal void FrameDropped() => Interlocked.Increment(ref dropped);
    internal void PreGateFrameDropped() => Interlocked.Increment(ref preGateDropped);
    internal void FinalSegmentDropped() => Interlocked.Increment(ref finalSegmentsDropped);
    internal void SegmentPublished() => Interlocked.Increment(ref published);
    internal void PublishFailed() => Interlocked.Increment(ref publishFailures);
    internal void PipelineFailed() => Interlocked.Increment(ref pipelineFailures);
    internal void KeepAliveFailed() => Interlocked.Increment(ref keepAliveFailures);

    private sealed class CallCounter(PipelineMetrics owner) : IDisposable
    {
        private int disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) == 0)
            {
                Interlocked.Decrement(ref owner.activeCalls);
            }
        }
    }
}

public sealed class CallCapacityException(int maxConcurrentCalls)
    : Exception($"The media bot is at its maximum capacity of {maxConcurrentCalls} concurrent calls.");

public sealed class MeetingCallCoordinator : IAsyncDisposable
{
    private readonly IGraphCallClient graph;
    private readonly ISpeechSessionFactory speechFactory;
    private readonly ICoachApiClient coach;
    private readonly MediaOptions media;
    private readonly PipelineMetrics metrics;
    private readonly ILoggerFactory loggerFactory;
    private readonly ILogger<MeetingCallCoordinator> logger;
    private readonly SemaphoreSlim capacity;
    private readonly ConcurrentDictionary<string, ActiveCall> calls = new(StringComparer.Ordinal);
    private readonly HashSet<string> callsEndedBeforeRegistration = new(StringComparer.Ordinal);
    private readonly object callStateLock = new();

    public MeetingCallCoordinator(
        IGraphCallClient graph,
        ISpeechSessionFactory speechFactory,
        ICoachApiClient coach,
        IOptions<MediaOptions> media,
        PipelineMetrics metrics,
        ILoggerFactory loggerFactory,
        ILogger<MeetingCallCoordinator> logger)
    {
        this.graph = graph;
        this.speechFactory = speechFactory;
        this.coach = coach;
        this.media = media.Value;
        this.metrics = metrics;
        this.loggerFactory = loggerFactory;
        this.logger = logger;
        capacity = new SemaphoreSlim(this.media.MaxConcurrentCalls, this.media.MaxConcurrentCalls);
        graph.CallEnded += OnCallEndedAsync;
    }

    public async Task<string> JoinAsync(JoinMeetingRequest request, CancellationToken cancellationToken)
    {
        if (!capacity.Wait(0))
        {
            logger.LogWarning("Rejected a join because all {Capacity} call slots are reserved.", media.MaxConcurrentCalls);
            throw new CallCapacityException(media.MaxConcurrentCalls);
        }

        var reservation = new CapacityReservation(capacity);
        ICallHandle? call = null;
        AudioPipeline? pipeline = null;
        IDisposable? counter = null;
        CancellationTokenSource? keepAliveStopping = null;
        Task? keepAlive = null;
        var resourcesCleaned = false;
        try
        {
            call = await graph.JoinAsync(request, cancellationToken).ConfigureAwait(false);
            await call.AwaitEstablishedAsync(
                TimeSpan.FromSeconds(media.EstablishmentTimeoutSeconds),
                cancellationToken).ConfigureAwait(false);
            await call.UpdateRecordingStatusAsync(cancellationToken).ConfigureAwait(false);

            pipeline = new AudioPipeline(
                media.AudioQueueCapacity,
                media.TranscriptQueueCapacity,
                speechFactory.Create(),
                coach,
                request.CoachSessionId,
                request.TeamsOnlineMeetingId,
                metrics,
                loggerFactory.CreateLogger<AudioPipeline>());
            await pipeline.StartAsync(cancellationToken).ConfigureAwait(false);
            call.AudioReceiver.Start(pipeline);
            counter = metrics.TrackCall();
            keepAliveStopping = new CancellationTokenSource();
            keepAlive = KeepAliveAsync(call, keepAliveStopping.Token);

            var active = new ActiveCall(
                call,
                pipeline,
                counter,
                reservation,
                keepAliveStopping,
                keepAlive);
            bool ended;
            lock (callStateLock)
            {
                ended = callsEndedBeforeRegistration.Remove(call.Id);
                if (!ended && !calls.TryAdd(call.Id, active))
                {
                    throw new InvalidOperationException($"Call '{call.Id}' is already active.");
                }
            }

            if (ended)
            {
                try
                {
                    await CleanupActiveAsync(call.Id, active).ConfigureAwait(false);
                }
                finally
                {
                    resourcesCleaned = true;
                }

                throw new InvalidOperationException($"Call '{call.Id}' ended while it was being initialized.");
            }

            logger.LogInformation("Call {CallId} established and passed the recording-status gate.", call.Id);
            return call.Id;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Join or recording-status gate failed.");
            if (!resourcesCleaned)
            {
                await CleanupJoinFailureAsync(
                    call,
                    pipeline,
                    counter,
                    reservation,
                    keepAliveStopping,
                    keepAlive).ConfigureAwait(false);
            }

            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        graph.CallEnded -= OnCallEndedAsync;
        List<Exception>? failures = null;
        string[] callIds;
        lock (callStateLock)
        {
            callIds = calls.Keys.ToArray();
        }

        foreach (var callId in callIds)
        {
            try
            {
                await RemoveCoreAsync(callId).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                (failures ??= []).Add(exception);
            }
        }

        capacity.Dispose();
        if (failures is not null)
        {
            throw new AggregateException("One or more calls could not be cleaned up.", failures);
        }
    }

    private async Task KeepAliveAsync(ICallHandle call, CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(media.KeepAliveIntervalMinutes));
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                try
                {
                    await call.KeepAliveAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception exception)
                {
                    metrics.KeepAliveFailed();
                    logger.LogError(exception, "Keepalive failed for call {CallId}.", call.Id);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private async Task OnCallEndedAsync(string callId)
    {
        ActiveCall? active;
        lock (callStateLock)
        {
            if (!calls.TryRemove(callId, out active))
            {
                callsEndedBeforeRegistration.Add(callId);
                return;
            }
        }

        try
        {
            await CleanupActiveAsync(callId, active).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            metrics.PipelineFailed();
            logger.LogError(exception, "Cleanup failed after Graph ended call {CallId}.", callId);
        }
    }

    private async Task<bool> RemoveCoreAsync(string callId)
    {
        ActiveCall? active;
        lock (callStateLock)
        {
            calls.TryRemove(callId, out active);
        }

        if (active is null)
        {
            return false;
        }

        await CleanupActiveAsync(callId, active).ConfigureAwait(false);
        return true;
    }

    private async Task CleanupActiveAsync(string callId, ActiveCall active)
    {
        var failures = new List<Exception>();
        active.KeepAliveStopping.Cancel();
        await TryCleanupAsync(() => new ValueTask(active.KeepAlive), failures).ConfigureAwait(false);
        active.KeepAliveStopping.Dispose();
        TryCleanup(active.Call.AudioReceiver.Dispose, failures);
        await TryCleanupAsync(active.Pipeline.DisposeAsync, failures).ConfigureAwait(false);
        TryCleanup(active.Counter.Dispose, failures);
        await TryCleanupAsync(
            () => new ValueTask(active.Call.DeleteAsync(CancellationToken.None)),
            failures).ConfigureAwait(false);
        await TryCleanupAsync(active.Call.DisposeAsync, failures).ConfigureAwait(false);
        TryCleanup(active.Reservation.Dispose, failures);
        logger.LogInformation("Cleaned up call {CallId}.", callId);
        if (failures.Count > 0)
        {
            throw new AggregateException($"Call '{callId}' cleanup failed.", failures);
        }
    }

    private static async Task CleanupJoinFailureAsync(
        ICallHandle? call,
        AudioPipeline? pipeline,
        IDisposable? counter,
        IDisposable reservation,
        CancellationTokenSource? keepAliveStopping,
        Task? keepAlive)
    {
        var ignored = new List<Exception>();
        keepAliveStopping?.Cancel();
        if (keepAlive is not null)
        {
            await TryCleanupAsync(() => new ValueTask(keepAlive), ignored).ConfigureAwait(false);
        }

        keepAliveStopping?.Dispose();
        TryCleanup(counter is null ? null : counter.Dispose, ignored);
        if (pipeline is not null)
        {
            await TryCleanupAsync(pipeline.DisposeAsync, ignored).ConfigureAwait(false);
        }

        if (call is not null)
        {
            TryCleanup(call.AudioReceiver.Dispose, ignored);
            await TryCleanupAsync(
                () => new ValueTask(call.DeleteAsync(CancellationToken.None)),
                ignored).ConfigureAwait(false);
            await TryCleanupAsync(call.DisposeAsync, ignored).ConfigureAwait(false);
        }

        TryCleanup(reservation.Dispose, ignored);
    }

    private static void TryCleanup(Action? cleanup, ICollection<Exception> failures)
    {
        try
        {
            cleanup?.Invoke();
        }
        catch (Exception exception)
        {
            failures.Add(exception);
        }
    }

    private static async Task TryCleanupAsync(
        Func<ValueTask> cleanup,
        ICollection<Exception> failures)
    {
        try
        {
            await cleanup().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            failures.Add(exception);
        }
    }

    private sealed record ActiveCall(
        ICallHandle Call,
        AudioPipeline Pipeline,
        IDisposable Counter,
        IDisposable Reservation,
        CancellationTokenSource KeepAliveStopping,
        Task KeepAlive);

    private sealed class CapacityReservation(SemaphoreSlim capacity) : IDisposable
    {
        private int disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) == 0)
            {
                capacity.Release();
            }
        }
    }
}
