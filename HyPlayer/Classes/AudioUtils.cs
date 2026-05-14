using System;
using System.Collections.Generic;
using System.Text;

namespace HyPlayer.Classes
{
    public static class AudioUtils
    {
        public static double DbToVolumePercent(double db)
        {
            var normalizedDb = Math.Min(db, 0.0);
            return Math.Pow(10, normalizedDb / 20.0);
        }
    }
}
