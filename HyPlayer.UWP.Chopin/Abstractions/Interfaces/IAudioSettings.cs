namespace HyPlayer.UWP.Chopin.Abstractions.Interfaces
{
    public interface IAudioSettings
    {
        string DefaultDeviceId { get; set; }
        double OutputVolume { get; set; }
    }
}
