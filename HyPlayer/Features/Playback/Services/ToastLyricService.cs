using System;
using System.ComponentModel;
using Windows.Data.Xml.Dom;
using Windows.UI.Notifications;
using HyPlayer.Application.Notifications;
using HyPlayer.Domain.Settings;

namespace HyPlayer.Features.Playback.Services;

/// <summary>A single live notification, independent of the lifetime of playback pages.</summary>
public sealed class ToastLyricService
{
    private const string Tag = "HyPlayerDesktopLyrics";
    private const string Group = "DesktopLyrics";
    private readonly object _gate = new();
    private readonly PlaybackStateService _state;
    private readonly LyricSettings _settings;
    private readonly INotificationService _notification;
    private ToastNotifier? _notifier;
    private ToastNotification? _toast;
    private long _lastSecond = -1;
    private bool _enabled;

    public ToastLyricService(PlaybackStateService state, LyricSettings settings, INotificationService notification)
    {
        _state = state;
        _settings = settings;
        _notification = notification;
        _settings.PropertyChanged += SettingsChanged;
    }

    public bool IsEnabled { get { lock (_gate) return _enabled; } }

    public void Toggle()
    {
        lock (_gate)
        {
            if (_enabled) { Stop(); return; }
            try
            {
                _notifier ??= ToastNotificationManager.CreateToastNotifier();
                if (_notifier.Setting != NotificationSetting.Enabled)
                {
                    _notification.ShowMessage("无法显示通知歌词", "请在 Windows 设置中允许 HyPlayer 发送通知。");
                    return;
                }
                _enabled = true;
                _state.PropertyChanged += StateChanged;
                Show();
            }
            catch (Exception ex) { Fail(ex); }
        }
    }

    private void SettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(LyricSettings.UseToastLyrics) || _settings.UseToastLyrics) return;
        lock (_gate) Stop();
    }

    private void StateChanged(object? sender, PropertyChangedEventArgs e)
    {
        lock (_gate)
        {
            if (!_enabled) return;
            if (e.PropertyName == nameof(PlaybackStateService.Position))
            {
                var second = (long)_state.Position.TotalSeconds;
                if (second == _lastSecond) return;
                _lastSecond = second;
            }
            else if (e.PropertyName is not (nameof(PlaybackStateService.LyricIndex)
                         or nameof(PlaybackStateService.LyricInfo) or nameof(PlaybackStateService.IsLyricPending)
                         or nameof(PlaybackStateService.NowPlayingSnapshot) or nameof(PlaybackStateService.Duration))) return;
            try
            {
                // A dismissed notification must stay dismissed; never recreate it on a position update.
                if (_notifier!.Update(CreateData(), Tag, Group) != NotificationUpdateResult.Succeeded) Stop();
            }
            catch (Exception ex) { Fail(ex); }
        }
    }

    private void Show()
    {
        DetachToast();
        var xml = new XmlDocument();
        xml.LoadXml("""
            <toast scenario="incomingCall" launch="desktopLyrics">
              <visual><binding template="ToastGeneric">
                <text>{Title}</text><text>{Lyric}</text><text>{Translation}</text>
                <progress value="{Progress}" valueStringOverride="{Duration}" status="{Position}" />
              </binding></visual>
              <audio silent="true" />
            </toast>
            """);
        _toast = new ToastNotification(xml)
        {
            Tag = Tag, Group = Group, Data = CreateData(),
            ExpirationTime = DateTimeOffset.Now.AddHours(1)
        };
        _toast.Dismissed += Dismissed;
        _toast.Failed += Failed;
        _notifier!.Show(_toast);
    }

    private NotificationData CreateData()
    {
        var content = ToastLyricContent.FromState(_state);
        var data = new NotificationData { SequenceNumber = 0 };
        data.Values["Title"] = content.Title;
        data.Values["Lyric"] = content.Lyric;
        data.Values["Translation"] = content.Translation;
        data.Values["Position"] = content.Position;
        data.Values["Duration"] = content.Duration;
        data.Values["Progress"] = content.Progress;
        return data;
    }

    private void Dismissed(ToastNotification sender, ToastDismissedEventArgs args)
    {
        lock (_gate)
        {
            if (!_enabled || !ReferenceEquals(sender, _toast)) return;
            try
            {
                if (args.Reason == ToastDismissalReason.TimedOut) Show();
                else Stop();
            }
            catch (Exception ex) { Fail(ex); }
        }
    }

    private void Failed(ToastNotification sender, ToastFailedEventArgs args)
    {
        lock (_gate)
            if (ReferenceEquals(sender, _toast)) Fail(args.ErrorCode);
    }

    private void DetachToast()
    {
        if (_toast is null) return;
        _toast.Dismissed -= Dismissed;
        _toast.Failed -= Failed;
        _toast = null;
    }

    private void Stop()
    {
        _enabled = false;
        _state.PropertyChanged -= StateChanged;
        _lastSecond = -1;
        var toast = _toast;
        DetachToast();
        try
        {
            if (toast != null) _notifier?.Hide(toast);
            ToastNotificationManager.History.Remove(Tag, Group);
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Remove toast lyrics: {ex}"); }
    }

    private void Fail(Exception error)
    {
        Stop();
        System.Diagnostics.Debug.WriteLine($"Toast lyrics: {error}");
        _notification.ShowMessage("通知歌词不可用", "请检查 Windows 通知设置后重试。");
    }
}
