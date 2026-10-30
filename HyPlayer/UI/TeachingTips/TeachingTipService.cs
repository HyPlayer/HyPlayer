using System;
using System.Collections.Generic;
using HyPlayer.Application.Threading;
using Microsoft.UI.Xaml.Controls;

namespace HyPlayer.UI.TeachingTips;

public sealed class TeachingTipService : ITeachingTipService
{
    private const int MessageSeconds = 3;
    private const int ActionSeconds = 10;
    private readonly IUIThreadDispatcher _uiThreadDispatcher;
    private int _secondCounter = MessageSeconds;
    private Action? _action;

    public TeachingTipService(IUIThreadDispatcher uiThreadDispatcher)
    {
        _uiThreadDispatcher = uiThreadDispatcher;
    }

    public Queue<TeachingTipItem> Items { get; } = new();
    public object? Tip { get; set; }

    public void Clear()
    {
        Items.Clear();
        _action = null;
    }

    public void InvokeAction()
    {
        var action = _action;
        _action = null;
        if (Tip is TeachingTip tip) tip.IsOpen = false;
        action?.Invoke();
    }

    public void Roll(bool passiveRoll = true)
    {
        if (passiveRoll && _secondCounter-- > 0) return;
        _secondCounter = MessageSeconds;

        if (Items.Count == 0)
        {
            _ = _uiThreadDispatcher.TryRunAsync(() =>
            {
                _action = null;
                if (Tip is TeachingTip tip) tip.IsOpen = false;
            });
            return;
        }

        _ = _uiThreadDispatcher.TryRunAsync(() =>
        {
            if (Items.Count == 0) return;
            var item = Items.Dequeue();
            if (Tip is not TeachingTip tip) return;
            _action = item.Action;
            if (item.Action is not null) _secondCounter = ActionSeconds;
            tip.Title = item.Title;
            tip.Subtitle = item.Subtitle ?? "";
            tip.ActionButtonContent = item.Action is null ? null : item.ActionText;
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