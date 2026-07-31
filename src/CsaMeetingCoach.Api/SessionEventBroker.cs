using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;
using CsaMeetingCoach.Contracts;
using CsaMeetingCoach.Core;

namespace CsaMeetingCoach.Api;

public sealed class SessionEventBroker : ISessionUpdatePublisher
{
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();
    private readonly ConcurrentDictionary<
        Guid,
        ConcurrentDictionary<Guid, Channel<string>>> _subscriptions = new();

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }

    public SessionSubscription Subscribe(Guid sessionId)
    {
        var subscriptionId = Guid.NewGuid();
        var channel = Channel.CreateBounded<string>(new BoundedChannelOptions(10)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false
        });
        var sessionSubscriptions = _subscriptions.GetOrAdd(
            sessionId,
            static _ => new ConcurrentDictionary<Guid, Channel<string>>());
        sessionSubscriptions[subscriptionId] = channel;

        return new SessionSubscription(
            channel.Reader,
            () => Remove(sessionId, subscriptionId));
    }

    public Task PublishAsync(MeetingSessionState session, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_subscriptions.TryGetValue(session.Id, out var subscribers))
        {
            return Task.CompletedTask;
        }

        var payload = SerializeSession(session);
        foreach (var channel in subscribers.Values)
        {
            channel.Writer.TryWrite(payload);
        }

        return Task.CompletedTask;
    }

    public string SerializeSession(MeetingSessionState session)
    {
        return JsonSerializer.Serialize(session, JsonOptions);
    }

    private void Remove(Guid sessionId, Guid subscriptionId)
    {
        if (!_subscriptions.TryGetValue(sessionId, out var subscribers))
        {
            return;
        }

        if (subscribers.TryRemove(subscriptionId, out var channel))
        {
            channel.Writer.TryComplete();
        }
    }
}

public sealed class SessionSubscription(
    ChannelReader<string> reader,
    Action dispose) : IAsyncDisposable
{
    public ChannelReader<string> Reader { get; } = reader;

    public ValueTask DisposeAsync()
    {
        dispose();
        return ValueTask.CompletedTask;
    }
}
