#nullable enable
using HyPlayer.Services.Abstractions;
using Microsoft.Gaming.XboxGameBar;

namespace HyPlayer.Services;

public sealed class GameBarWidgetService : IGameBarWidgetService
{
    public XboxGameBarWidget? Widget { get; set; }

    public void ClearReference(XboxGameBarWidget owner)
    {
        if (ReferenceEquals(Widget, owner)) Widget = null;
    }
}
