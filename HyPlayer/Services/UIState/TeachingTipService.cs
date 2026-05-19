using System;
using System.Collections.Generic;
using System.Diagnostics;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.Core;
using Windows.Foundation;
using Windows.UI.Core;

using HyPlayer.Services.Abstractions;

namespace HyPlayer.Services;

public sealed class TeachingTipService : ITeachingTipService
{
    private int _secondCounter = 3;

    public Queue<KeyValuePair<string, string?>> Items { get; } = new();
    public object? Tip { get; set; }

    public void Clear() => Items.Clear();

    public void Roll(bool passiveRoll = true)
    {
        if (passiveRoll && _secondCounter-- > 0) return;
        _secondCounter = 3;

        if (Items.Count == 0)
        {
            _ = InvokeOnUIThread(() =>
            {
                if (Tip is TeachingTip tip) tip.IsOpen = false;
            });
            return;
        }

        _ = InvokeOnUIThread(() =>
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

    private static IAsyncAction? InvokeOnUIThread(Action action)
    {
        try
        {
            if (CoreApplication.Views.Count > 0)
                return CoreApplication.MainView.Dispatcher.RunAsync(
                    CoreDispatcherPriority.Normal,
                    () => action());
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"TeachingTip dispatch failed: {ex.Message}");
        }

        return null;
    }
}
