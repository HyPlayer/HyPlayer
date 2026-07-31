using System;

namespace HyPlayer.Application.Notifications;

public interface IPlaylistCollectionChangeNotifier
{
    event EventHandler? Changed;
    void NotifyChanged();
}