#nullable enable
using Microsoft.Gaming.XboxGameBar;

namespace HyPlayer.Features.Widgets.Services;

public interface IGameBarWidgetService
{
    XboxGameBarWidget? Widget { get; set; }
    void ClearReference(XboxGameBarWidget owner);
}