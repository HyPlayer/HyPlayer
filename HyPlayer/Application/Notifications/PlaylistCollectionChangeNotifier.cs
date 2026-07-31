using System;

namespace HyPlayer.Application.Notifications;

public sealed class PlaylistCollectionChangeNotifier : IPlaylistCollectionChangeNotifier
{
    public event EventHandler? Changed;

    public void NotifyChanged()
    {
        Changed?.Invoke(this, EventArgs.Empty);
    }
}