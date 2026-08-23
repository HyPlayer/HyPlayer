using CommunityToolkit.Mvvm.ComponentModel;

namespace HyPlayer.Features.Playback.Services;

/// <summary>
///     Projection of expanded-player-specific state derived from <see cref="PlaybackSurfaceStore" />.
/// </summary>
public sealed partial class ExpandedPlayerSurfaceProjection : ObservableObject
{
    private bool _isActive;

    public bool IsActive
    {
        get => _isActive;
        set => SetProperty(ref _isActive, value);
    }
}
