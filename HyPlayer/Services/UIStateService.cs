#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.DependencyInjection;
using HyPlayer.Classes;
using HyPlayer.Services.Abstractions;
using Kawazu;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.Core;
using Windows.Foundation;
using Windows.System.Display;
using Windows.UI.Core;

namespace HyPlayer.Services;

/// <summary>
/// UI 状态服务实现
/// </summary>
public class UIStateService : IUIStateService
{
    private bool _isExpanded;
    private int _teachingTipSecondCounter = 3;

    public object? PageExpandedPlayer { get; set; }
    public object? PageMain { get; set; }
    public object? BarPlayBar { get; set; }
    public object? PageBase { get; set; }
    public object? GlobalTip { get; set; }
    public object? XboxGameBarWidget { get; set; }
    public KawazuConverter? KawazuConv { get; set; }

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            _isExpanded = value;
            Ioc.Default.GetRequiredService<Setting>().OnPropertyChanged("playbarBackgroundAcrylic");
        }
    }

    public bool IsInBackground { get; set; }
    public bool ShowLyricSound { get; set; } = true;
    public bool ShowLyricTrans { get; set; } = true;
    public bool NavigatingBack { get; set; }
    public int PlaybarSecondCounter { get; set; }
    public bool PlaybarIsVisible { get; set; } = true;
    public DisplayRequest DisplayRequest { get; } = new();
    public BrushManagement BrushManagement { get; } = new();
    public List<string> ErrorMessageList { get; } = [];
    public ObservableCollection<string> Logs { get; } = [];
    public Queue<KeyValuePair<string, string?>> TeachingTipList { get; } = new();

    public event Action? OnEnterForegroundFromBackground;
    public event Action<bool>? OnPlaybarVisibilityChanged;

    public void InvokeEnterForeground() => OnEnterForegroundFromBackground?.Invoke();
    public void InvokePlaybarVisibilityChanged(bool isActivated) => OnPlaybarVisibilityChanged?.Invoke(isActivated);

    public void RollTeachingTip(bool passiveRoll = true)
    {
        if (passiveRoll && _teachingTipSecondCounter-- > 0) return;
        _teachingTipSecondCounter = 3;
        if (TeachingTipList.Count == 0)
        {
            InvokeOnUIThread(() =>
            {
                if (GlobalTip is TeachingTip tip) tip.IsOpen = false;
            });
            return;
        }

        InvokeOnUIThread(() =>
        {
            if (TeachingTipList.Count == 0) return;
            var (title, subtitle) = TeachingTipList.Dequeue();
            if (GlobalTip is not TeachingTip tip) return;
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

    public void ChangePlaybarVisibility()
    {
        var setting = Ioc.Default.GetRequiredService<Setting>();
        if (++PlaybarSecondCounter >= setting.AutoHidePlaybarTime)
        {
            if (PlaybarIsVisible)
            {
                OnPlaybarVisibilityChanged?.Invoke(false);
                PlaybarIsVisible = false;
            }
        }
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
        catch
        {
            // Ignore
        }
        return null;
    }
}
