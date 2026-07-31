using CsaMeetingCoach.BotService.Configuration;
using CsaMeetingCoach.BotService.Media;
using Microsoft.CognitiveServices.Speech;
using Microsoft.CognitiveServices.Speech.Audio;
using Microsoft.Extensions.Options;

namespace CsaMeetingCoach.BotService.Integration;

public sealed class AzureSpeechSessionFactory(
    IOptions<SpeechOptions> options,
    ILoggerFactory loggerFactory) : ISpeechSessionFactory
{
    public ISpeechSession Create() =>
        new AzureSpeechSession(options.Value, loggerFactory.CreateLogger<AzureSpeechSession>());
}

public sealed class AzureSpeechSession : ISpeechSession
{
    private readonly PushAudioInputStream stream;
    private readonly AudioConfig audioConfig;
    private readonly SpeechRecognizer recognizer;
    private readonly ILogger<AzureSpeechSession> logger;
    private bool disposed;

    public AzureSpeechSession(SpeechOptions options, ILogger<AzureSpeechSession> logger)
    {
        this.logger = logger;
        var speechConfig = SpeechConfig.FromSubscription(options.SubscriptionKey, options.Region);
        speechConfig.SpeechRecognitionLanguage = options.Language;
        var format = AudioStreamFormat.GetWaveFormatPCM(16_000, 16, 1);
        stream = AudioInputStream.CreatePushStream(format);
        audioConfig = AudioConfig.FromStreamInput(stream);
        recognizer = new SpeechRecognizer(speechConfig, audioConfig);
        recognizer.Recognized += OnRecognized;
        recognizer.Canceled += OnCanceled;
        recognizer.SessionStopped += OnSessionStopped;
    }

    public event Action<FinalTranscript>? FinalRecognized;
    public event Action<Exception>? Failed;

    public Task StartAsync(CancellationToken cancellationToken) =>
        recognizer.StartContinuousRecognitionAsync().WaitAsync(cancellationToken);

    public void Write(ReadOnlySpan<byte> pcmS16Le16KhzMono)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        stream.Write(pcmS16Le16KhzMono.ToArray());
    }

    public async ValueTask DisposeAsync()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        var failures = new List<Exception>();
        try
        {
            await recognizer.StopContinuousRecognitionAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            failures.Add(exception);
        }

        recognizer.Recognized -= OnRecognized;
        recognizer.Canceled -= OnCanceled;
        recognizer.SessionStopped -= OnSessionStopped;
        TryDispose(recognizer, failures);
        TryDispose(audioConfig, failures);
        TryDispose(stream, failures);
        if (failures.Count > 0)
        {
            throw new AggregateException("Azure Speech cleanup failed.", failures);
        }
    }

    private void OnRecognized(object? sender, SpeechRecognitionEventArgs args)
    {
        if (args.Result.Reason != ResultReason.RecognizedSpeech
            || string.IsNullOrWhiteSpace(args.Result.Text))
        {
            return;
        }

        var handlers = FinalRecognized;
        if (handlers is null)
        {
            return;
        }

        var transcript = new FinalTranscript(
            args.Result.ResultId,
            args.Result.Text,
            DateTimeOffset.UtcNow);
        foreach (Action<FinalTranscript> handler in handlers.GetInvocationList())
        {
            try
            {
                handler(transcript);
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "A final-recognition callback failed.");
            }
        }
    }

    private void OnCanceled(object? sender, SpeechRecognitionCanceledEventArgs args)
    {
        if (!disposed)
        {
            NotifyFailed(new InvalidOperationException(
                $"Azure Speech recognition was canceled ({args.Reason}): {args.ErrorDetails}"));
        }
    }

    private void OnSessionStopped(object? sender, SessionEventArgs args)
    {
        if (!disposed)
        {
            NotifyFailed(new InvalidOperationException("Azure Speech recognition stopped unexpectedly."));
        }
    }

    private void NotifyFailed(Exception exception)
    {
        var handlers = Failed;
        if (handlers is null)
        {
            logger.LogError(exception, "Azure Speech recognition failed without a pipeline subscriber.");
            return;
        }

        foreach (Action<Exception> handler in handlers.GetInvocationList())
        {
            try
            {
                handler(exception);
            }
            catch (Exception callbackException)
            {
                logger.LogError(callbackException, "An Azure Speech failure callback failed.");
            }
        }
    }

    private static void TryDispose(IDisposable resource, ICollection<Exception> failures)
    {
        try
        {
            resource.Dispose();
        }
        catch (Exception exception)
        {
            failures.Add(exception);
        }
    }
}
