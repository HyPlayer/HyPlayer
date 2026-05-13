#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using HyPlayer.Classes;
using HyPlayer.Services.Abstractions;
using HyPlayer.Services.Playback.Messages;
using Kawazu;
using Microsoft.UI.Xaml.Controls;
using System.Timers;
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
    private readonly Setting _setting;
    private readonly NotificationDispatcher _dispatcher;

    private bool _isExpanded;
    private int _teachingTipSecondCounter = 3;

    public UIStateService(Setting setting, NotificationDispatcher dispatcher)
    {
        _setting = setting;
        _dispatcher = dispatcher;
    }

    public object? PageExpandedPlayer { get; set; }
    public object? PageCompactPlayer { get; set; }
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
            _setting.OnPropertyChanged("playbarBackgroundAcrylic");
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
    public Timer GlobalSecondTimer { get; } = new(1000)
    {
        AutoReset = true,
        Enabled = true,
    };

    public void InvokeEnterForeground() => _dispatcher.Publish(new EnterForegroundFromBackgroundNotification());
    public void InvokePlaybarVisibilityChanged(bool isActivated) => _dispatcher.Publish(new PlaybarVisibilityChangedNotification(isActivated));

    public void ClearReferences(object owner)
    {
        if (ReferenceEquals(PageExpandedPlayer, owner)) PageExpandedPlayer = null;
        if (ReferenceEquals(PageCompactPlayer, owner)) PageCompactPlayer = null;
        if (ReferenceEquals(PageMain, owner)) PageMain = null;
        if (ReferenceEquals(BarPlayBar, owner)) BarPlayBar = null;
        if (ReferenceEquals(PageBase, owner)) PageBase = null;
        if (ReferenceEquals(GlobalTip, owner)) GlobalTip = null;
        if (ReferenceEquals(XboxGameBarWidget, owner)) XboxGameBarWidget = null;
    }

    public void RollTeachingTip(bool passiveRoll = true)
    {
        if (passiveRoll && _teachingTipSecondCounter-- > 0) return;
        _teachingTipSecondCounter = 3;
        if (TeachingTipList.Count == 0)
        {
            _ = InvokeOnUIThread(() =>
            {
                if (GlobalTip is TeachingTip tip) tip.IsOpen = false;
            });
            return;
        }

        _ = InvokeOnUIThread(() =>
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
        if (++PlaybarSecondCounter >= _setting.AutoHidePlaybarTime)
        {
            if (PlaybarIsVisible)
            {
                InvokePlaybarVisibilityChanged(false);
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
