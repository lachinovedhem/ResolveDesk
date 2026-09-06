using System.Collections.Concurrent;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using ResolveDesk.Application;
using ResolveDesk.Core;

namespace ResolveDesk.Infrastructure;

/// <summary>
/// In-process fan-out to connected SSE clients.
///
/// Deliberately in-process: notifications are already durable in PostgreSQL, so this only accelerates
/// delivery to a tab that happens to be open. Behind several instances a client connected to node A
/// misses a live push from node B, and picks it up from the database on its next poll or reload —
/// degraded, never lost. Moving to Redis pub/sub would be a drop-in replacement of this class.
/// </summary>
public sealed class NotificationHub(ILogger<NotificationHub> logger) : INotificationHub
{
    // One user can have several tabs open, so subscribers are per connection, not per user.
    private readonly ConcurrentDictionary<long, ConcurrentDictionary<Guid, Channel<Notification>>> _subscribers = new();

    public void Publish(Notification notification)
    {
        if (!_subscribers.TryGetValue(notification.UserId, out var connections)) return;

        foreach (var (id, channel) in connections)
        {
            // Bounded, drop-newest: a client that has stopped reading must not grow the queue without
            // limit. It still has the durable copy in the database.
            if (!channel.Writer.TryWrite(notification))
                logger.LogDebug("Dropped a live notification for a saturated connection {ConnectionId}.", id);
        }
    }

    public async IAsyncEnumerable<Notification> SubscribeAsync(
        long userId, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        var id = Guid.NewGuid();
        var channel = Channel.CreateBounded<Notification>(new BoundedChannelOptions(64)
        {
            FullMode = BoundedChannelFullMode.DropWrite,
            SingleReader = true,
        });

        var connections = _subscribers.GetOrAdd(userId, _ => new ConcurrentDictionary<Guid, Channel<Notification>>());
        connections[id] = channel;

        try
        {
            await foreach (var notification in channel.Reader.ReadAllAsync(ct))
                yield return notification;
        }
        finally
        {
            connections.TryRemove(id, out _);
            // Leaving empty maps behind would be a slow leak on a long-running instance.
            if (connections.IsEmpty) _subscribers.TryRemove(userId, out _);
        }
    }
}

/// <summary>Persists a notification, then pushes it to whoever is listening. Never throws at callers.</summary>
public sealed class Notifier(
    INotificationRepository repository,
    INotificationHub hub,
    ILogger<Notifier> logger) : INotifier
{
    public async Task NotifyAsync(
        long userId, NotificationKind kind, long? ticketId, string title, string body,
        CancellationToken ct = default)
    {
        try
        {
            var id = await repository.AddAsync(userId, kind, ticketId, title, body, ct);
            hub.Publish(new Notification(id, userId, kind, ticketId, title, body, DateTime.UtcNow, null));
        }
        catch (Exception ex)
        {
            // A failed notification must not roll back the assignment or comment that caused it.
            logger.LogWarning(ex, "Could not deliver a {Kind} notification to user {UserId}.", kind, userId);
        }
    }
}
