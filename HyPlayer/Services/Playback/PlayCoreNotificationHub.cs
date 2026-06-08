using Depository.Abstraction.Interfaces;
using Depository.Abstraction.Interfaces.NotificationHub;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace HyPlayer.Services.Playback;

public sealed class PlayCoreNotificationHub(IDepository depository) : INotificationHub
{
    public async Task PublishNotificationAsync<TNotification>(
        TNotification notification,
        CancellationToken ctk = default)
    {
        ctk.ThrowIfCancellationRequested();

        var subscribers = depository
            .ResolveDependencies(typeof(IEnumerable<INotificationSubscriber<TNotification>>))
            .OfType<INotificationSubscriber<TNotification>>()
            .ToList();

        foreach (var subscriber in subscribers)
        {
            ctk.ThrowIfCancellationRequested();
            await subscriber.HandleNotificationAsync(notification, ctk).ConfigureAwait(false);
        }
    }

    public Task<List<TResult>> PublishNotificationWithResultAsync<TNotification, TResult>(
        TNotification notification,
        CancellationToken ctk = default)
    {
        ctk.ThrowIfCancellationRequested();
        return Task.FromResult(new List<TResult>());
    }
}
