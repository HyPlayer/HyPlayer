using HyPlayer.Services.Abstractions;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using Windows.ApplicationModel.Core;
using Windows.Foundation;
using Windows.UI.Core;
using Windows.UI.Xaml;

namespace HyPlayer.UI.TeachingTips;

public sealed class TeachingTipService : ITeachingTipService
{
    private int _secondCounter = 3;

    public Queue<KeyValuePair<string, string?>> Items { get; } = new();
    public TeachingTip? Tip { get; set; }

    public void Clear() => Items.Clear();

    public void Roll(bool passiveRoll = true)
    {
        if (passiveRoll && _secondCounter-- > 0) return;
        _secondCounter = 3;

        if (Items.Count == 0)
        {
            RunOnUIThread(() =>
            {
                if (Tip is TeachingTip tip) tip.IsOpen = false;
            });
            return;
        }

        RunOnUIThread(() =>
        {
            if (Items.Count == 0) return;
            var (title, subtitle) = Items.Dequeue();
            Tip.Title = title;
            Tip.Subtitle = subtitle ?? "";
            if (!Tip.IsOpen)
            {
                Tip.IsOpen = true;
            }
            else
            {
                Tip.IsOpen = false;
                Tip.IsOpen = true;
            }
        });
    }

    private void RunOnUIThread(Action action)
    {
        _ = CoreApplication.MainView.Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () => { action(); });
    }
}
