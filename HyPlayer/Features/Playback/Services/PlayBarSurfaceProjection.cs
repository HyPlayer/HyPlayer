using CommunityToolkit.Mvvm.ComponentModel;

namespace HyPlayer.Features.Playback.Services;

/// <summary>
///     Projection of PlayBar-specific visibility booleans derived from <see cref="PlaybackSurfaceStore" />.
/// </summary>
public sealed class PlayBarSurfaceProjection : ObservableObject
{
    private bool _isExpanded;

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (SetProperty(ref _isExpanded, value))
            {
                OnPropertyChanged(nameof(ShowExpandButton));
                OnPropertyChanged(nameof(ShowCollapseButton));
                OnPropertyChanged(nameof(ShowSongInfo));
                OnPropertyChanged(nameof(ShowAdvancedOperations));
            }
        }
    }

    public bool ShowExpandButton => !IsExpanded;
    public bool ShowCollapseButton => IsExpanded;
    public bool ShowSongInfo => !IsExpanded;
    public bool ShowAdvancedOperations => IsExpanded;
}
