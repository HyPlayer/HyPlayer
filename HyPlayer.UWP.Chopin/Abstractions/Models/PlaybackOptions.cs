namespace HyPlayer.UWP.Chopin.Abstractions.Models
{
    public class PlaybackOptions
    {
        public bool AutoPlay { get; set; } = true;
        public bool SetAsPrimarySource { get; set; } = false;
        public double Volume { get; set; } = 1d;
        public double AudioGain { get; set; } = 1d;
    }
}
