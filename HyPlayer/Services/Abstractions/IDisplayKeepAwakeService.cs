namespace HyPlayer.Services.Abstractions;

public interface IDisplayKeepAwakeService
{
    void RequestActive();
    void RequestRelease();
}
