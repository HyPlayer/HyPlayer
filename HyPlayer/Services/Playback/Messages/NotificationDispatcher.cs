using System;
using Microsoft.Extensions.DependencyInjection;

namespace HyPlayer.Services.Playback.Messages;

public interface INotificationHandler<in TNotification>
{
    void Handle(TNotification notification);
}

public sealed class NotificationDispatcher
{
    private readonly IServiceProvider _serviceProvider;

    public NotificationDispatcher(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public void Publish<TNotification>(TNotification notification)
    {
        foreach (var handler in _serviceProvider.GetServices<INotificationHandler<TNotification>>())
            handler.Handle(notification);
    }
}