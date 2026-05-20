using System;

namespace HyPlayer.Services.Abstractions;

public interface IPlaylistCollectionChangeNotifier
{
    event EventHandler? Changed;
    void NotifyChanged();
}
