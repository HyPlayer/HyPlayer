using HyPlayer.Domain.Music;
using HyPlayer.Services.Abstractions;
using System.Collections.Generic;

namespace HyPlayer.Infrastructure.Diagnostics
{
    public class DumpInfo
    {
        public PlaybackCurrentItemSnapshot CurrentSong { get; set; }
        public string CurrentPlaySource { get; set; }
        public NCUser CurrentUser { get; set; }
        public string DeviceId { get; set; }
        public bool IsInBackground { get; set; }
        public bool IsLowCache { get; set; }
        public List<string> ErrorMessageList { get; set; }
    }
}
