using CommunityToolkit.Mvvm.ComponentModel;

namespace HyPlayer.Features.Playback.DynamicCover;

public sealed partial class AppleMusicCoverStatus : ObservableObject
{
    [ObservableProperty] public partial bool IsAvailable { get; set; } = true;

    [ObservableProperty] public partial string Message { get; set; } = string.Empty;

    internal void MarkAvailable()
    {
        IsAvailable = true;
        Message = string.Empty;
    }

    internal void MarkUnavailable(string reason)
    {
        IsAvailable = false;
        Message = $"无法获取到 Apple Music 动态封面（{reason}）";
    }
}
