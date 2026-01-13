namespace HyPlayer.UWP.Chopin.Abstractions.Interfaces
{
    public interface IAudioSettings
    {
        string DefaultDeviceId { get; set; }
        double OutputVolume { get; set; }
        bool AutoFallback { get; set; }
        bool EnableFFTProcessing { get; set; }
    }
}
