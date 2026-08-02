using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using HyPlayer.Platform.Playback.LocalProvider;

namespace HyPlayer.Features.Library;

public partial class LocalMusicPageViewModel : ObservableObject
{
    [ObservableProperty]
    public partial string NotificationText { get; set; } = string.Empty;

    public ObservableCollection<LocalSong> LocalItems { get; } = [];
}
