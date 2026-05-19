namespace HyPlayer.Services.Abstractions;

public interface IPlayBarAutoHideService
{
    int SecondCounter { get; set; }
    bool IsVisible { get; set; }
    void Tick();
    void Show();
}
