using System;
using System.Collections.Generic;
using System.Text;

namespace HyPlayer.Classes
{
    public class NCPlayList
    {
        public long BookCount { get; set; }
        public string Cover { get; set; }
        public NCUser Creator { get; set; }
        public string Description { get; set; }
        public string Name { get; set; }
        public long PlayCount { get; set; }
        public string PlaylistId { get; set; }
        public bool HasSubscribed { get; set; }
        public long TrackCount { get; set; }
        public DateTime CreateTime { get; set; }
        public DateTime UpdateTime { get; set; }
        public bool IsDailyRecommend { get; set; }
    }
}
