using CsaMeetingCoach.BotService.Configuration;
using CsaMeetingCoach.BotService.Media;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CsaMeetingCoach.BotService.Tests;

public sealed class AudioPipelineTests
{
    [Fact]
    public async Task BoundedPipelineDropsFramesWithoutBlockingProducer()
    {
        var speech = new FakeSpeechSession { BlockWrites = true };
        var metrics = new PipelineMetrics();
        await using var pipeline = CreatePipeline(1, speech, new FakeCoachClient(), metrics);
        await pipeline.StartAsync(CancellationToken.None);

        Assert.True(pipeline.TryWrite([1]));
        Assert.True(speech.WriteEntered.Wait(TimeSpan.FromSeconds(2)));
        Assert.True(pipeline.TryWrite([2]));
        Assert.False(pipeline.TryWrite([3]));
        Assert.Equal(1, metrics.Snapshot().FramesDropped);

        speech.ReleaseWrites.Set();
    }

    [Fact]
    public async Task ClosedGateDiscardsFramesWithoutTranscription()
    {
        var speech = new FakeSpeechSession();
        var metrics = new PipelineMetrics();
        await using var pipeline = CreatePipeline(1, speech, new FakeCoachClient(), metrics);

        Assert.False(pipeline.TryWrite([1]));
        Assert.Equal(0, speech.WriteCount);
        Assert.Equal(1, metrics.Snapshot().PreGateFramesDropped);
    }

    [Fact]
    public async Task SpeechFailureClosesGateAndMarksPipelineFailed()
    {
        var speech = new FakeSpeechSession();
        var metrics = new PipelineMetrics();
        await using var pipeline = CreatePipeline(1, speech, new FakeCoachClient(), metrics);
        await pipeline.StartAsync(CancellationToken.None);

        speech.RaiseFailure(new InvalidOperationException("speech stopped"));

        Assert.False(pipeline.TryWrite([1]));
        Assert.Equal(1, metrics.Snapshot().PipelineFailures);
    }

    [Fact]
    public async Task PublishFailureClosesGateAndMarksPipelineFailed()
    {
        var speech = new FakeSpeechSession();
        var coach = new FakeCoachClient
        {
            PublishException = new HttpRequestException("Coach API unavailable")
        };
        var metrics = new PipelineMetrics();
        await using var pipeline = CreatePipeline(1, speech, coach, metrics);
        await pipeline.StartAsync(CancellationToken.None);

        speech.RaiseFinal(new FinalTranscript("recognition-1", "Final text", DateTimeOffset.UtcNow));

        Assert.True(SpinWait.SpinUntil(
            () => metrics.Snapshot().PipelineFailures == 1,
            TimeSpan.FromSeconds(2)));
        Assert.False(pipeline.TryWrite([1]));
        Assert.Equal(1, metrics.Snapshot().PublishFailures);
    }

    [Fact]
    public async Task FinalRecognitionUsesStableSourceIdAndSkipsEmptyText()
    {
        var sessionId = Guid.NewGuid();
        var firstCoach = new FakeCoachClient();
        var firstSpeech = new FakeSpeechSession();
        await using (var pipeline = CreatePipeline(2, firstSpeech, firstCoach, new PipelineMetrics(), sessionId))
        {
            await pipeline.StartAsync(CancellationToken.None);
            firstSpeech.RaiseFinal(new FinalTranscript("recognition-1", "Final text", DateTimeOffset.UtcNow));
            firstSpeech.RaiseFinal(new FinalTranscript("recognition-2", " ", DateTimeOffset.UtcNow));
        }

        var secondCoach = new FakeCoachClient();
        var secondSpeech = new FakeSpeechSession();
        await using (var pipeline = CreatePipeline(2, secondSpeech, secondCoach, new PipelineMetrics(), sessionId))
        {
            await pipeline.StartAsync(CancellationToken.None);
            secondSpeech.RaiseFinal(new FinalTranscript("recognition-1", "Final text", DateTimeOffset.UtcNow));
        }

        Assert.Equal(firstCoach.SourceSegmentId, secondCoach.SourceSegmentId);
        Assert.NotEqual(Guid.Empty, firstCoach.SourceSegmentId);
        Assert.Equal(1, firstCoach.PublishCount);
    }

    [Fact]
    public async Task EstablishmentAndRecordingCompleteBeforeTranscriptionAndSocketStart()
    {
        var operations = new List<string>();
        var graph = new FakeGraphCallClient(operations);
        var speech = new FakeSpeechSession(operations);
        await using var coordinator = CreateCoordinator(graph, speech);

        var callId = await coordinator.JoinAsync(Request(), CancellationToken.None);

        Assert.Equal("call-1", callId);
        Assert.Equal(
            ["join", "established", "recording", "speech-start", "socket-start"],
            operations);
    }

    [Fact]
    public async Task RecordingFailureDoesNotStartSpeechOrSocket()
    {
        var operations = new List<string>();
        var graph = new FakeGraphCallClient(operations, recordingFails: true);
        var speech = new FakeSpeechSession(operations);
        await using var coordinator = CreateCoordinator(graph, speech);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => coordinator.JoinAsync(Request(), CancellationToken.None));

        Assert.Equal(["join", "established", "recording"], operations);
        Assert.Equal(0, speech.WriteCount);
    }

    [Fact]
    public async Task CapacityIsReservedBeforeASecondGraphJoin()
    {
        var operations = new List<string>();
        var graph = new FakeGraphCallClient(operations);
        var speech = new FakeSpeechSession(operations);
        await using var coordinator = CreateCoordinator(graph, speech, maxConcurrentCalls: 1);
        await coordinator.JoinAsync(Request(), CancellationToken.None);

        await Assert.ThrowsAsync<CallCapacityException>(
            () => coordinator.JoinAsync(Request(), CancellationToken.None));

        Assert.Equal(1, graph.JoinCount);
    }

    [Fact]
    public async Task CallEndingDuringInitializationIsNotRegisteredOrLeaked()
    {
        var call = new RaceCallHandle();
        var graph = new RaceGraphCallClient(call);
        await using var coordinator = CreateCoordinator(graph, new FakeSpeechSession());

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => coordinator.JoinAsync(Request(), CancellationToken.None));

        Assert.True(call.Disposed);
    }

    private static AudioPipeline CreatePipeline(
        int capacity,
        ISpeechSession speech,
        ICoachApiClient coach,
        PipelineMetrics metrics,
        Guid? sessionId = null) =>
        new(
            capacity,
            capacity,
            speech,
            coach,
            sessionId ?? Guid.NewGuid(),
            "meeting",
            metrics,
            NullLogger<AudioPipeline>.Instance);

    private static MeetingCallCoordinator CreateCoordinator(
        IGraphCallClient graph,
        ISpeechSession speech,
        int maxConcurrentCalls = 2) =>
        new(
            graph,
            new FakeSpeechFactory(speech),
            new FakeCoachClient(),
            Options.Create(new MediaOptions
            {
                AudioQueueCapacity = 2,
                TranscriptQueueCapacity = 2,
                MaxConcurrentCalls = maxConcurrentCalls
            }),
            new PipelineMetrics(),
            NullLoggerFactory.Instance,
            NullLogger<MeetingCallCoordinator>.Instance);

    private static JoinMeetingRequest Request() =>
        new("tenant", "chat", "organizer", Guid.NewGuid(), "meeting");

    private sealed class FakeSpeechFactory(ISpeechSession session) : ISpeechSessionFactory
    {
        public ISpeechSession Create() => session;
    }

    private sealed class FakeSpeechSession : ISpeechSession
    {
        private readonly List<string>? operations;

        public FakeSpeechSession(List<string>? operations = null) => this.operations = operations;

        public event Action<FinalTranscript>? FinalRecognized;
        public event Action<Exception>? Failed;
        public bool BlockWrites { get; init; }
        public int WriteCount { get; private set; }
        public ManualResetEventSlim WriteEntered { get; } = new();
        public ManualResetEventSlim ReleaseWrites { get; } = new();

        public Task StartAsync(CancellationToken cancellationToken)
        {
            operations?.Add("speech-start");
            return Task.CompletedTask;
        }

        public void Write(ReadOnlySpan<byte> pcmS16Le16KhzMono)
        {
            WriteCount++;
            WriteEntered.Set();
            if (BlockWrites)
            {
                ReleaseWrites.Wait(TimeSpan.FromSeconds(5));
            }
        }

        public void RaiseFinal(FinalTranscript transcript) => FinalRecognized?.Invoke(transcript);
        public void RaiseFailure(Exception exception) => Failed?.Invoke(exception);

        public ValueTask DisposeAsync()
        {
            ReleaseWrites.Set();
            WriteEntered.Dispose();
            ReleaseWrites.Dispose();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FakeCoachClient : ICoachApiClient
    {
        public Guid SourceSegmentId { get; private set; }
        public int PublishCount { get; private set; }
        public Exception? PublishException { get; init; }

        public Task PublishAsync(
            Guid coachSessionId,
            string teamsOnlineMeetingId,
            Guid sourceSegmentId,
            FinalTranscript transcript,
            CancellationToken cancellationToken)
        {
            SourceSegmentId = sourceSegmentId;
            PublishCount++;
            return PublishException is null
                ? Task.CompletedTask
                : Task.FromException(PublishException);
        }
    }

    private sealed class FakeGraphCallClient(
        List<string> operations,
        bool recordingFails = false) : IGraphCallClient
    {
        public bool Enabled => true;
        public bool Configured => true;
        public bool Ready => true;
        public int JoinCount { get; private set; }
        public event Func<string, Task>? CallEnded
        {
            add { }
            remove { }
        }

        public Task<ICallHandle> JoinAsync(
            JoinMeetingRequest request,
            CancellationToken cancellationToken)
        {
            JoinCount++;
            operations.Add("join");
            return Task.FromResult<ICallHandle>(new FakeCallHandle(operations, recordingFails));
        }

        public Task<HttpResponseMessage> ProcessNotificationAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakeCallHandle(
        List<string> operations,
        bool recordingFails) : ICallHandle
    {
        public string Id => "call-1";
        public IAudioReceiver AudioReceiver { get; } = new FakeReceiver(operations);

        public Task AwaitEstablishedAsync(TimeSpan timeout, CancellationToken cancellationToken)
        {
            operations.Add("established");
            return Task.CompletedTask;
        }

        public Task UpdateRecordingStatusAsync(CancellationToken cancellationToken)
        {
            operations.Add("recording");
            return recordingFails
                ? Task.FromException(new InvalidOperationException("recording failed"))
                : Task.CompletedTask;
        }

        public Task KeepAliveAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task DeleteAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakeReceiver(List<string> operations) : IAudioReceiver
    {
        public void Start(IAudioFrameSink sink) => operations.Add("socket-start");
        public void Dispose() { }
    }

    private sealed class RaceGraphCallClient(RaceCallHandle call) : IGraphCallClient
    {
        public bool Enabled => true;
        public bool Configured => true;
        public bool Ready => true;
        public event Func<string, Task>? CallEnded;

        public async Task<ICallHandle> JoinAsync(
            JoinMeetingRequest request,
            CancellationToken cancellationToken)
        {
            if (CallEnded is not null)
            {
                await CallEnded(call.Id);
            }

            return call;
        }

        public Task<HttpResponseMessage> ProcessNotificationAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class RaceCallHandle : ICallHandle
    {
        public string Id => "race-call";
        public IAudioReceiver AudioReceiver { get; } = new FakeReceiver([]);
        public bool Disposed { get; private set; }

        public Task AwaitEstablishedAsync(TimeSpan timeout, CancellationToken cancellationToken) =>
            Task.CompletedTask;
        public Task UpdateRecordingStatusAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task KeepAliveAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task DeleteAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }
}
