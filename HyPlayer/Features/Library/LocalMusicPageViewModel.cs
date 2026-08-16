using CommunityToolkit.Mvvm.ComponentModel;
using HyPlayer.Platform.Playback.LocalProvider;
using ObservableCollections;

namespace HyPlayer.Features.Library;

public partial class LocalMusicPageViewModel : ObservableObject
{
    public LocalMusicPageViewModel()
    {
        LocalItemsView = LocalItems.ToNotifyCollectionChanged();
    }

    [ObservableProperty]
    public partial string NotificationText { get; set; } = string.Empty;

    public ObservableList<LocalSong> LocalItems { get; } = [];
    public NotifyCollectionChangedSynchronizedViewList<LocalSong> LocalItemsView { get; }
}
