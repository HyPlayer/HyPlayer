namespace HyPlayer.Platform.SystemServices;

public interface IDisplayKeepAwakeService
{
    void RequestActive();
    void RequestRelease();
}
