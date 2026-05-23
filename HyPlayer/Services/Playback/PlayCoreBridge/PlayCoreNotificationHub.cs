using Depository.Abstraction.Interfaces;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace HyPlayer.Services.Playback.PlayCoreBridge;

public sealed class PlayCoreNotificationHub : INotificationHub
{
    public Task PublishNotificationAsync<TNotification>(
        TNotification notification,
        CancellationToken ctk = default)
    {
        ctk.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    public Task<List<TResult>> PublishNotificationWithResultAsync<TNotification, TResult>(
        TNotification notification,
        CancellationToken ctk = default)
    {
        ctk.ThrowIfCancellationRequested();
        return Task.FromResult(new List<TResult>());
    }
}
