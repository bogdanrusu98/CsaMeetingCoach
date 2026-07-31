using System.Net;
using System.Runtime.InteropServices;
using System.Collections.Concurrent;
using Azure.Core;
using Azure.Identity;
using CsaMeetingCoach.BotService.Configuration;
using CsaMeetingCoach.BotService.Media;
using Microsoft.Graph.Communications.Calls;
using Microsoft.Graph.Communications.Calls.Media;
using Microsoft.Graph.Communications.Client;
using Microsoft.Graph.Communications.Client.Authentication;
using Microsoft.Graph.Communications.Common.Telemetry;
using Microsoft.Graph.Communications.Resources;
using Microsoft.Graph.Models;
using Microsoft.Skype.Bots.Media;
using Microsoft.Extensions.Options;

namespace CsaMeetingCoach.BotService.Graph;

public sealed class GraphCallClient : IGraphCallClient
{
    private readonly ICommunicationsClient? client;
    private readonly ILogger<GraphCallClient> logger;
    private readonly PipelineMetrics metrics;
    private readonly ConcurrentDictionary<long, Task> callbackTasks = new();
    private long callbackTaskId;

    public GraphCallClient(
        IOptions<GraphOptions> graphOptions,
        IOptions<MediaOptions> mediaOptions,
        ILogger<GraphCallClient> logger,
        PipelineMetrics metrics)
    {
        this.logger = logger;
        this.metrics = metrics;
        var graph = graphOptions.Value;
        Enabled = graph.Enabled;
        Configured = Enabled;
        if (!Enabled)
        {
            return;
        }

        try
        {
            var media = mediaOptions.Value;
            var sdkLogger = new GraphLogger(
                nameof(CsaMeetingCoach),
                Array.Empty<object>(),
                redirectToTrace: true,
                obfuscationConfiguration: null!);
            var credential = new ClientSecretCredential(graph.TenantId, graph.AppId, graph.ClientSecret);
            var builder = new CommunicationsClientBuilder(nameof(CsaMeetingCoach), graph.AppId, sdkLogger)
                .SetAuthentication(graph.AppId, new GraphTokenProvider(credential))
                .SetNotificationUrl(graph.NotificationUrl!)
                .SetServiceBaseUrl(graph.ServiceBaseUrl)
                .SetMediaPlatformSettings(new MediaPlatformSettings
                {
                    ApplicationId = graph.AppId,
                    MediaPlatformInstanceSettings = new MediaPlatformInstanceSettings
                    {
                        ServiceFqdn = media.ServiceFqdn,
                        InstancePublicIPAddress = IPAddress.Parse(media.PublicIpAddress),
                        InstancePublicPort = media.PublicPort,
                        InstanceInternalPort = media.InternalPort,
                        CertificateThumbprint = media.CertificateThumbprint
                    }
                });

            client = builder.Build();
            Ready = true;
            client.Calls().OnUpdated += (sender, args) =>
            {
                foreach (var removed in args.RemovedResources)
                {
                    TrackCallback(DispatchCallEndedAsync(removed.Id));
                }
            };
        }
        catch (Exception exception)
        {
            metrics.PipelineFailed();
            logger.LogCritical(exception, "Graph communications client initialization failed.");
        }
    }

    public bool Enabled { get; }
    public bool Configured { get; }
    public bool Ready { get; }

    public event Func<string, Task>? CallEnded;

    public async Task<ICallHandle> JoinAsync(
        JoinMeetingRequest request,
        CancellationToken cancellationToken)
    {
        EnsureEnabled();
        var mediaSession = client!.CreateMediaSession(
            new AudioSocketSettings
            {
                StreamDirections = StreamDirection.Recvonly,
                SupportedAudioFormat = AudioFormat.Pcm16K
            },
            videoSocketSettings: Array.Empty<VideoSocketSettings>(),
            vbssSocketSettings: null,
            dataSocketSettings: null,
            mediaSessionId: request.MediaSessionId ?? Guid.NewGuid());
        var receiver = new TeamsAudioReceiver(
            mediaSession.AudioSocket,
            logger,
            metrics);

        try
        {
            var parameters = new JoinMeetingParameters(
                new ChatInfo
                {
                    ThreadId = request.ChatId,
                    MessageId = request.MessageId
                },
                new OrganizerMeetingInfo
                {
                    Organizer = new IdentitySet
                    {
                        User = new Identity { Id = request.OrganizerObjectId }
                    }
                },
                mediaSession)
            {
                TenantId = request.TenantId
            };

            var call = await client.Calls().AddAsync(
                parameters,
                Guid.NewGuid(),
                cancellationToken).ConfigureAwait(false);
            return new GraphCallHandle(call, mediaSession, receiver);
        }
        catch
        {
            receiver.Dispose();
            mediaSession.Dispose();
            throw;
        }
    }

    public Task<HttpResponseMessage> ProcessNotificationAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        EnsureEnabled();
        return client!.ProcessNotificationAsync(request);
    }

    public async ValueTask DisposeAsync()
    {
        if (client is not null)
        {
            await client.TerminateAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
        }

        var callbacks = callbackTasks.Values.ToArray();
        if (callbacks.Length > 0)
        {
            await Task.WhenAll(callbacks).ConfigureAwait(false);
        }

        if (client is not null)
        {
            client.Dispose();
        }
    }

    private void TrackCallback(Task task)
    {
        var id = Interlocked.Increment(ref callbackTaskId);
        callbackTasks[id] = task;
        _ = task.ContinueWith(
            (_, state) => RemoveCallback((long)state!),
            id,
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private void RemoveCallback(long id) => callbackTasks.TryRemove(id, out Task? _);

    private async Task DispatchCallEndedAsync(string callId)
    {
        var handlers = CallEnded;
        if (handlers is null)
        {
            return;
        }

        foreach (Func<string, Task> handler in handlers.GetInvocationList())
        {
            try
            {
                await handler(callId).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                metrics.PipelineFailed();
                logger.LogError(exception, "Call-ended callback failed for call {CallId}.", callId);
            }
        }
    }

    private void EnsureEnabled()
    {
        if (!Enabled)
        {
            throw new InvalidOperationException("Graph communications are disabled.");
        }

        if (!Ready)
        {
            throw new InvalidOperationException("Graph communications are not ready.");
        }
    }

    private sealed class GraphTokenProvider(TokenCredential credential) : ITokenProvider
    {
        public async Task<string> AcquireTokenAsync(string resource)
        {
            var scope = $"{resource.TrimEnd('/')}/.default";
            var token = await credential.GetTokenAsync(
                new TokenRequestContext([scope]),
                CancellationToken.None).ConfigureAwait(false);
            return token.Token;
        }
    }

    private sealed class GraphCallHandle(
        ICall call,
        ILocalMediaSession mediaSession,
        TeamsAudioReceiver receiver) : ICallHandle
    {
        public string Id => call.Id;
        public IAudioReceiver AudioReceiver => receiver;

        public async Task AwaitEstablishedAsync(
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            var established = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);

            void Observe(Call? resource)
            {
                switch (resource?.State)
                {
                    case CallState.Established:
                        established.TrySetResult();
                        break;
                    case CallState.Terminated:
                    case CallState.Terminating:
                        established.TrySetException(new InvalidOperationException(
                            $"Call '{call.Id}' entered terminal state '{resource.State}' before establishment."));
                        break;
                }
            }

            ResourceEventHandler<ICall, Call> updated = (_, args) => Observe(args.NewResource);
            call.OnUpdated += updated;
            try
            {
                Observe(call.Resource);
                await established.Task.WaitAsync(timeout, cancellationToken).ConfigureAwait(false);
            }
            catch (TimeoutException exception)
            {
                throw new TimeoutException(
                    $"Call '{call.Id}' was not established within {timeout}.",
                    exception);
            }
            finally
            {
                call.OnUpdated -= updated;
            }
        }

        public Task UpdateRecordingStatusAsync(CancellationToken cancellationToken) =>
            call.UpdateRecordingStatusAsync(RecordingStatus.Recording, cancellationToken);

        public Task KeepAliveAsync(CancellationToken cancellationToken) =>
            call.KeepAliveAsync(cancellationToken);

        public Task DeleteAsync(CancellationToken cancellationToken) =>
            call.DeleteAsync(handleHttpNotFoundInternally: true, cancellationToken);

        public ValueTask DisposeAsync()
        {
            receiver.Dispose();
            mediaSession.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}

public sealed class TeamsAudioReceiver : IAudioReceiver
{
    private readonly IAudioSocket socket;
    private readonly ILogger logger;
    private readonly PipelineMetrics metrics;
    private IAudioFrameSink? sink;
    private int disposed;
    private int sinkAttached;

    public TeamsAudioReceiver(
        IAudioSocket socket,
        ILogger logger,
        PipelineMetrics metrics)
    {
        this.socket = socket;
        this.logger = logger;
        this.metrics = metrics;
        socket.AudioMediaReceived += OnAudioMediaReceived;
    }

    public void Start(IAudioFrameSink sink)
    {
        ArgumentNullException.ThrowIfNull(sink);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        if (Interlocked.Exchange(ref sinkAttached, 1) != 0)
        {
            throw new InvalidOperationException("The audio receiver was already started.");
        }

        this.sink = sink;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) == 0)
        {
            socket.AudioMediaReceived -= OnAudioMediaReceived;
            sink = null;
            Volatile.Write(ref sinkAttached, 0);
        }
    }

    private void OnAudioMediaReceived(object? sender, AudioMediaReceivedEventArgs args)
    {
        try
        {
            var buffer = args.Buffer;
            try
            {
                var managed = GC.AllocateUninitializedArray<byte>(checked((int)buffer.Length));
                Marshal.Copy(buffer.Data, managed, 0, managed.Length);
                var currentSink = sink;
                if (currentSink is null)
                {
                    metrics.PreGateFrameDropped();
                    logger.LogWarning("Dropped an audio frame because the recording gate is closed.");
                }
                else if (!currentSink.TryWrite(managed))
                {
                    // The sink records and logs bounded-queue drops.
                }
            }
            finally
            {
                try
                {
                    buffer.Dispose();
                }
                catch (Exception exception)
                {
                    metrics.PipelineFailed();
                    logger.LogError(exception, "Failed to dispose a Teams audio buffer.");
                }
            }
        }
        catch (Exception exception)
        {
            metrics.PipelineFailed();
            logger.LogError(exception, "Failed to copy or enqueue a Teams audio frame.");
        }
    }
}
