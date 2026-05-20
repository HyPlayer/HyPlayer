using HyPlayer.Services.Abstractions;
using System;

namespace HyPlayer.Services.Notifications;

public sealed class PlaylistCollectionChangeNotifier : IPlaylistCollectionChangeNotifier
{
    public event EventHandler? Changed;

    public void NotifyChanged()
    {
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
