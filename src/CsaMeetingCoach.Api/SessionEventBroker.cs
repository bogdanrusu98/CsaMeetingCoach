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
        ConcurrentDictionary<Guid, SessionEventRegistration>> _subscriptions = new();

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }

    public SessionSubscription Subscribe(Guid sessionId, SessionRole role)
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
            static _ => new ConcurrentDictionary<Guid, SessionEventRegistration>());
        sessionSubscriptions[subscriptionId] = new SessionEventRegistration(role, channel);

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

        string? hostPayload = null;
        string? memberPayload = null;
        foreach (var subscriber in subscribers.Values)
        {
            var payload = subscriber.Role == SessionRole.Host
                ? hostPayload ??= SerializeSession(session, SessionRole.Host)
                : memberPayload ??= SerializeSession(session, SessionRole.Member);
            subscriber.Channel.Writer.TryWrite(payload);
        }

        return Task.CompletedTask;
    }

    public string SerializeSession(MeetingSessionState session, SessionRole role)
    {
        var view = role == SessionRole.Host
            ? (object)session
            : SessionViewProjector.ForMember(session);
        return JsonSerializer.Serialize(view, JsonOptions);
    }

    public void CloseSession(Guid sessionId)
    {
        if (!_subscriptions.TryRemove(sessionId, out var subscribers))
        {
            return;
        }

        foreach (var subscriber in subscribers.Values)
        {
            subscriber.Channel.Writer.TryComplete();
        }
    }

    private void Remove(Guid sessionId, Guid subscriptionId)
    {
        if (!_subscriptions.TryGetValue(sessionId, out var subscribers))
        {
            return;
        }

        if (subscribers.TryRemove(subscriptionId, out var registration))
        {
            registration.Channel.Writer.TryComplete();
        }
    }

    private sealed record SessionEventRegistration(
        SessionRole Role,
        Channel<string> Channel);
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
