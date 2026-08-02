using System.Collections.Generic;
using HyPlayer.Application.Threading;
using Microsoft.UI.Xaml.Controls;

namespace HyPlayer.UI.TeachingTips;

public sealed class TeachingTipService : ITeachingTipService
{
    private readonly IUIThreadDispatcher _uiThreadDispatcher;
    private int _secondCounter = 3;

    public TeachingTipService(IUIThreadDispatcher uiThreadDispatcher)
    {
        _uiThreadDispatcher = uiThreadDispatcher;
    }

    public Queue<KeyValuePair<string, string?>> Items { get; } = new();
    public object? Tip { get; set; }

    public void Clear()
    {
        Items.Clear();
    }

    public void Roll(bool passiveRoll = true)
    {
        if (passiveRoll && _secondCounter-- > 0) return;
        _secondCounter = 3;

        if (Items.Count == 0)
        {
            _ = _uiThreadDispatcher.TryRunAsync(() =>
            {
                if (Tip is TeachingTip tip) tip.IsOpen = false;
            });
            return;
        }

        _ = _uiThreadDispatcher.TryRunAsync(() =>
        {
            if (Items.Count == 0) return;
            var (title, subtitle) = Items.Dequeue();
            if (Tip is not TeachingTip tip) return;
            tip.Title = title;
            tip.Subtitle = subtitle ?? "";
            if (!tip.IsOpen)
            {
                tip.IsOpen = true;
            }
            else
            {
                tip.IsOpen = false;
                tip.IsOpen = true;
            }
        });
    }
}