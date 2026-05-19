#nullable enable
using Microsoft.Gaming.XboxGameBar;

namespace HyPlayer.Services.Abstractions;

public interface IGameBarWidgetService
{
    XboxGameBarWidget? Widget { get; set; }
    void ClearReference(XboxGameBarWidget owner);
}
