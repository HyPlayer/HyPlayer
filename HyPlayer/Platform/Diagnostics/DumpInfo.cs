using System.Collections.Generic;
using HyPlayer.Domain;
using HyPlayer.Features.Playback.Services;

namespace HyPlayer.Platform.Diagnostics;

public class DumpInfo
{
    public PlaybackCurrentItemSnapshot CurrentSong { get; set; }
    public string CurrentPlaySource { get; set; }
    public CommentUserInfo CurrentUser { get; set; }
    public string DeviceId { get; set; }
    public bool IsInBackground { get; set; }
    public bool IsLowCache { get; set; }
    public List<string> ErrorMessageList { get; set; }
}