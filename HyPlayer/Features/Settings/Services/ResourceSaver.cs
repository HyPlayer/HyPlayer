#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Windows.System.Power;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using CommunityToolkit.Mvvm.DependencyInjection;
using HyPlayer.Application.Notifications;
using HyPlayer.Application.State;
using HyPlayer.Application.Threading;
using HyPlayer.Domain.Settings;
using HyPlayer.Features.Lyrics.Effects;
using HyPlayer.Features.Playback.Models;
using HyPlayer.Platform.Network;

namespace HyPlayer.Features.Settings.Services;

public static class ResourceSaver
{
    private enum SaverKind
    {
        Network,
        Power
    }

    private sealed class SettingChange
    {
        public required string Key { get; init; }
        public required Func<string> Read { get; init; }
        public required Action<string> Write { get; init; }
        public required string Target { get; init; }
    }

    private sealed class SaverOption
    {
        public required string Title { get; init; }
        public string? Description { get; init; }
        public required SettingChange[] Changes { get; init; }
    }

    private static readonly TimeSpan SlowRequestThreshold = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan SlowLoadThreshold = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan QuietWindow = TimeSpan.FromSeconds(90);
    private static readonly TimeSpan ProbeInterval = TimeSpan.FromSeconds(20);
    private static readonly SemaphoreSlim DialogGate = new(1, 1);
    private static readonly List<(DateTime Time, bool Slow)> Requests = new();
    private static readonly object SlowLock = new();

    private static volatile bool _attached;
    private static bool _networkActive;
    private static bool _powerActive;
    private static bool _networkPrompted;
    private static bool _powerPrompted;
    private static int _probing;
    private static DateTime _lastSlow = DateTime.MinValue;

    private static PlaybackSettings Playback => Ioc.Default.GetRequiredService<PlaybackSettings>();
    private static UISettings UI => Ioc.Default.GetRequiredService<UISettings>();
    private static ApiSettings Api => Ioc.Default.GetRequiredService<ApiSettings>();
    private static LyricSettings Lyric => Ioc.Default.GetRequiredService<LyricSettings>();

    private const string RelaxedNetworkKey = "RelaxedNetwork";
    private const string LyricBlurKey = "LyricMaximumBlur";

    private static SettingChange AudioRateChange => new()
    {
        Key = nameof(PlaybackSettings.AudioRate),
        Read = () => Playback.AudioRate,
        Write = value => Playback.AudioRate = value,
        Target = "standard"
    };

    private static SettingChange RelaxedNetworkChange =>
        BoolChange(RelaxedNetworkKey, () => RetryPolicies.RelaxedNetwork,
            value => RetryPolicies.RelaxedNetwork = value, true);

    private static SettingChange[] StreamingChanges => [AudioRateChange, RelaxedNetworkChange];

    private static void ReapplyRelaxedNetwork()
    {
        foreach (var (key, _, target) in Parse(GetBackup(SaverKind.Network)))
            if (key == RelaxedNetworkKey)
                RetryPolicies.RelaxedNetwork = target == "1";
    }

    private static SettingChange NoImageChange =>
        BoolChange(nameof(UISettings.NoImage), () => UI.NoImage, value => UI.NoImage = value, true);

    private static SettingChange GreedyLoadChange =>
        BoolChange(nameof(ApiSettings.GreedilyLoadPlayContainerItems), () => Api.GreedilyLoadPlayContainerItems,
            value => Api.GreedilyLoadPlayContainerItems = value, false);

    private static SettingChange AmllChange =>
        BoolChange(nameof(LyricSettings.EnableAmllTtmlDb), () => Lyric.EnableAmllTtmlDb,
            value => Lyric.EnableAmllTtmlDb = value, false);

    private static SettingChange ShowSpectrumChange =>
        BoolChange(nameof(PlaybackSettings.ShowSpectrum), () => Playback.ShowSpectrum,
            value => Playback.ShowSpectrum = value, false);

    private static SettingChange EnableFftChange =>
        BoolChange(nameof(PlaybackSettings.EnableFFT), () => Playback.EnableFFT,
            value => Playback.EnableFFT = value, false);

    private static SettingChange AlbumBreathChange =>
        BoolChange(nameof(UISettings.ExpandAlbumBreath), () => UI.ExpandAlbumBreath,
            value => UI.ExpandAlbumBreath = value, false);

    private static SettingChange PlaybarBreathChange =>
        BoolChange(nameof(UISettings.PlaybarBackgroundBreath), () => UI.PlaybarBackgroundBreath,
            value => UI.PlaybarBackgroundBreath = value, false);

    public static void Attach()
    {
        if (_attached) return;
        _attached = true;
        PowerManager.EnergySaverStatusChanged += (_, _) => OnPowerChanged();
        PowerManager.PowerSupplyStatusChanged += (_, _) => OnPowerChanged();
        PowerManager.BatteryStatusChanged += (_, _) => OnPowerChanged();
        Ioc.Default.GetRequiredService<IAppLifecycleStateService>().EnteredForeground += (_, _) => OnPowerChanged();

        _networkActive = HasBackup(SaverKind.Network);
        _powerActive = HasBackup(SaverKind.Power);
        if (_networkActive) StartProbing();
        if (_networkActive) ReapplyRelaxedNetwork();

        _ = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(12));
            OnPowerChanged();
        });
    }

    public static void ReportLoad(TimeSpan elapsed)
    {
        if (!_attached || elapsed < SlowLoadThreshold) return;
        lock (SlowLock) _lastSlow = DateTime.UtcNow;
        SuspectWeakNetwork();
    }

    public static void ReportRequest(TimeSpan elapsed, bool failed)
    {
        if (!_attached) return;
        var slow = failed || elapsed > SlowRequestThreshold;
        bool trigger;
        lock (SlowLock)
        {
            var now = DateTime.UtcNow;
            Requests.RemoveAll(request => now - request.Time > QuietWindow);
            Requests.Add((now, slow));
            if (slow) _lastSlow = now;
            var slowCount = Requests.Count(request => request.Slow);
            trigger = slowCount >= 3 && slowCount * 2 >= Requests.Count;
        }

        if (trigger) SuspectWeakNetwork();
    }

    private static void SuspectWeakNetwork()
    {
        if (_networkActive || _networkPrompted || !Playback.PromptWeakNetwork) return;
        _ = RunOnUiAsync(PromptNetworkAsync);
    }

    private static async Task PromptNetworkAsync()
    {
        if (_networkActive || _networkPrompted) return;
        _networkPrompted = true;
        var result = await ShowDialogAsync(
            "网络状况较差",
            "检测到当前网络较慢，加载可能较为卡顿。可以临时做以下调整，网络恢复后会自动还原：",
            BuildNetworkOptions(),
            () => Playback.PromptWeakNetwork = false);
        if (result is { Count: > 0 })
        {
            Apply(SaverKind.Network, result);
            _networkActive = true;
        }

        StartProbing();
    }

    private static List<SaverOption> BuildNetworkOptions()
    {
        var options = new List<SaverOption>();
        if (Playback.AudioRate != AudioRateChange.Target)
            options.Add(new SaverOption
            {
                Title = "将在线播放音质降为 标准 (128k)",
                Changes = [AudioRateChange]
            });
        if (!UI.NoImage)
            options.Add(new SaverOption
            {
                Title = "不加载封面图片",
                Description = "新打开的页面生效",
                Changes = [NoImageChange]
            });
        if (Api.GreedilyLoadPlayContainerItems)
            options.Add(new SaverOption { Title = "关闭歌单贪婪加载", Changes = [GreedyLoadChange] });
        if (Lyric.EnableAmllTtmlDb)
            options.Add(new SaverOption { Title = "暂停从 AMLL 歌词库获取逐字歌词", Changes = [AmllChange] });
        if (!RetryPolicies.RelaxedNetwork)
            options.Add(new SaverOption
            {
                Title = "提高请求重试次数与超时时间",
                Description = "加载超时从 25 秒放宽到 60 秒，获取播放链接最多重试 6 次",
                Changes = [RelaxedNetworkChange]
            });
        return options;
    }

    private static void OnPowerChanged()
    {
        _ = RunOnUiAsync(async () =>
        {
            if (IsPowerSaving())
            {
                if (!_powerActive && !_powerPrompted) await PromptPowerAsync();
                return;
            }

            _powerPrompted = false;
            if (!_powerActive) return;
            _powerActive = false;
            if (Restore(SaverKind.Power))
                Ioc.Default.GetRequiredService<INotificationService>().ShowMessage("已恢复电源", "已还原节能模式下调整的设置");
        });
    }

    private static bool IsPowerSaving()
    {
        try
        {
            if (PowerManager.EnergySaverStatus == EnergySaverStatus.On) return true;
            return PowerManager.BatteryStatus == BatteryStatus.Discharging &&
                   PowerManager.PowerSupplyStatus == PowerSupplyStatus.NotPresent;
        }
        catch
        {
            return false;
        }
    }

    private static async Task PromptPowerAsync()
    {
        _powerPrompted = true;
        if (!Playback.PromptPowerSaving) return;
        var options = BuildPowerOptions();
        if (options.Count == 0) return;
        var reason = PowerManager.EnergySaverStatus == EnergySaverStatus.On ? "Windows 已开启节能模式" : "设备正在使用电池供电";
        var result = await ShowDialogAsync(
            "节能模式",
            $"{reason}。可以临时关闭以下较为耗电的功能，插入电源或关闭节能后会自动还原：",
            options,
            () => Playback.PromptPowerSaving = false);
        if (result is not { Count: > 0 } || !IsPowerSaving()) return;
        Apply(SaverKind.Power, result);
        _powerActive = true;
    }

    private static SettingChange ModelAnalysisChange =>
        BoolChange(nameof(AutoMixSettings.ModelsSuspended),
            () => Ioc.Default.GetRequiredService<AutoMixSettings>().ModelsSuspended,
            value => Ioc.Default.GetRequiredService<AutoMixSettings>().ModelsSuspended = value, true);

    private static ILyricRenderSettingsService LyricEffects =>
        Ioc.Default.GetRequiredService<ILyricRenderSettingsService>();

    private static SettingChange LyricBlurChange => new()
    {
        Key = LyricBlurKey,
        Read = () => LyricEffects.Current.Line.MaximumBlur.ToString(CultureInfo.InvariantCulture),
        Write = value =>
        {
            if (!float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var blur)) return;
            var current = LyricEffects.Current;
            LyricEffects.Commit(current with { Line = current.Line with { MaximumBlur = blur } });
        },
        Target = "0"
    };

    private static List<SaverOption> BuildPowerOptions()
    {
        var options = new List<SaverOption>();
        if (Playback.TransitionId == "mix" && Ioc.Default.GetRequiredService<ModelManager>().HasUsableModel)
            options.Add(new SaverOption
            {
                Title = "暂停自动混音的模型分析",
                Description = "改用轻量的算法分析节拍，已分析过的歌曲不受影响",
                Changes = [ModelAnalysisChange]
            });
        if (Playback.ShowSpectrum || Playback.EnableFFT)
            options.Add(new SaverOption
            {
                Title = "关闭频谱可视化与音频信息输出",
                Changes = [ShowSpectrumChange, EnableFftChange]
            });
        if (LyricEffects.Current.Line.MaximumBlur > 0)
            options.Add(new SaverOption { Title = "关闭歌词模糊效果", Changes = [LyricBlurChange] });
        var breath = new List<SettingChange>();
        if (UI.PlaybarBackgroundBreath) breath.Add(PlaybarBreathChange);
        if (UI.ExpandAlbumBreath) breath.Add(AlbumBreathChange);
        if (breath.Count > 0)
            options.Add(new SaverOption { Title = "关闭封面与播放栏的呼吸动画", Changes = [.. breath] });
        if (Api.GreedilyLoadPlayContainerItems)
            options.Add(new SaverOption { Title = "关闭歌单贪婪加载", Changes = [GreedyLoadChange] });
        return options;
    }

    private static void StartProbing()
    {
        if (Interlocked.Exchange(ref _probing, 1) == 1) return;
        _ = Task.Run(async () =>
        {
            var good = 0;
            try
            {
                while (true)
                {
                    await Task.Delay(ProbeInterval);
                    var latency = await ProbeAsync();
                    bool quiet;
                    lock (SlowLock) quiet = DateTime.UtcNow - _lastSlow > QuietWindow;
                    good = quiet && latency is { } elapsed && elapsed < TimeSpan.FromSeconds(1) ? good + 1 : 0;
                    if (good < 3) continue;
                    var restored = await RunOnUiAsync(() =>
                    {
                        _networkPrompted = false;
                        lock (SlowLock) Requests.Clear();
                        if (!_networkActive) return Task.CompletedTask;
                        _networkActive = false;
                        if (Restore(SaverKind.Network))
                            Ioc.Default.GetRequiredService<INotificationService>()
                                .ShowMessage("网络已恢复", "已还原弱网时调整的设置");
                        return Task.CompletedTask;
                    });
                    if (restored) return;
                }
            }
            finally
            {
                Interlocked.Exchange(ref _probing, 0);
            }
        });
    }

    private static async Task<TimeSpan?> ProbeAsync()
    {
        try
        {
            var client = Ioc.Default.GetRequiredService<HttpClient>();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(6));
            using var request = new HttpRequestMessage(HttpMethod.Head, "https://interface.music.163.com/");
            request.Options.Set(NetworkWatchHandler.ProbeKey, true);
            var started = Stopwatch.GetTimestamp();
            using var response =
                await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token);
            return Stopwatch.GetElapsedTime(started);
        }
        catch
        {
            return null;
        }
    }

    private static async Task<List<SaverOption>?> ShowDialogAsync(string title, string message,
        List<SaverOption> options, Action disablePrompt)
    {
        if (options.Count == 0) return null;
        await DialogGate.WaitAsync();
        try
        {
            var panel = new StackPanel { Spacing = 8, MaxWidth = 460 };
            panel.Children.Add(new TextBlock
            {
                Text = message,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 4)
            });
            var boxes = new List<(CheckBox Box, SaverOption Option)>();
            foreach (var option in options)
            {
                var content = new StackPanel();
                content.Children.Add(new TextBlock { Text = option.Title, TextWrapping = TextWrapping.Wrap });
                if (option.Description is { } description)
                    content.Children.Add(new TextBlock
                    {
                        Text = description,
                        FontSize = 12,
                        Opacity = 0.7,
                        TextWrapping = TextWrapping.Wrap
                    });
                var box = new CheckBox { Content = content, IsChecked = true };
                boxes.Add((box, option));
                panel.Children.Add(box);
            }

            var dontAsk = new CheckBox
            {
                Content = "下次不再弹出此提示",
                IsChecked = false,
                Margin = new Thickness(0, 8, 0, 0)
            };
            panel.Children.Add(dontAsk);
            panel.Children.Add(new TextBlock
            {
                Text = "可以在 设置 → 播放设置 → 弱网与节能 中重新开启",
                FontSize = 12,
                Opacity = 0.6,
                TextWrapping = TextWrapping.Wrap
            });

            var dialog = new ContentDialog
            {
                Title = title,
                Content = new ScrollViewer
                {
                    Content = panel,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto
                },
                PrimaryButtonText = "应用",
                CloseButtonText = "保持现状",
                DefaultButton = ContentDialogButton.Primary
            };

            ContentDialogResult result;
            for (var attempt = 0;; attempt++)
                try
                {
                    result = await dialog.ShowAsync();
                    break;
                }
                catch when (attempt < 30)
                {
                    await Task.Delay(TimeSpan.FromSeconds(5));
                }

            if (dontAsk.IsChecked == true) disablePrompt();
            if (result != ContentDialogResult.Primary) return [];
            return boxes.Where(entry => entry.Box.IsChecked == true).Select(entry => entry.Option).ToList();
        }
        catch
        {
            return null;
        }
        finally
        {
            DialogGate.Release();
        }
    }

    private static void Apply(SaverKind kind, List<SaverOption> options)
    {
        var entries = new List<string>();
        foreach (var change in options.SelectMany(option => option.Changes))
        {
            string original;
            try
            {
                original = change.Read();
                if (original == change.Target) continue;
                change.Write(change.Target);
            }
            catch
            {
                continue;
            }

            entries.Add($"{change.Key}={Uri.EscapeDataString(original)}|{Uri.EscapeDataString(change.Target)}");
        }

        SetBackup(kind, string.Join(";", entries));
    }

    private static bool Restore(SaverKind kind)
    {
        var entries = Parse(GetBackup(kind));
        SetBackup(kind, string.Empty);
        if (entries.Count == 0) return false;
        var catalog = Catalog();
        foreach (var (key, original, target) in entries)
        {
            if (!catalog.TryGetValue(key, out var change)) continue;
            try
            {
                if (change.Read() == target) change.Write(original);
            }
            catch
            {
            }
        }

        return true;
    }

    private static bool HasBackup(SaverKind kind) => !string.IsNullOrEmpty(GetBackup(kind));

    private static string GetBackup(SaverKind kind) =>
        kind == SaverKind.Network ? Playback.WeakNetworkBackup : Playback.PowerSavingBackup;

    private static void SetBackup(SaverKind kind, string value)
    {
        if (kind == SaverKind.Network) Playback.WeakNetworkBackup = value;
        else Playback.PowerSavingBackup = value;
    }

    private static List<(string Key, string Original, string Target)> Parse(string text)
    {
        var result = new List<(string, string, string)>();
        foreach (var entry in text.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = entry.IndexOf('=');
            var bar = entry.LastIndexOf('|');
            if (eq <= 0 || bar <= eq) continue;
            result.Add((entry[..eq], Uri.UnescapeDataString(entry[(eq + 1)..bar]),
                Uri.UnescapeDataString(entry[(bar + 1)..])));
        }

        return result;
    }

    private static Dictionary<string, SettingChange> Catalog()
    {
        SettingChange[] changes =
        [
            NoImageChange, GreedyLoadChange, AmllChange, ShowSpectrumChange, EnableFftChange, AlbumBreathChange,
            PlaybarBreathChange
        ];
        var map = new Dictionary<string, SettingChange>();
        foreach (var change in changes) map[change.Key] = change;
        foreach (var change in StreamingChanges) map[change.Key] = change;
        map[ModelAnalysisChange.Key] = ModelAnalysisChange;
        map[LyricBlurChange.Key] = LyricBlurChange;
        return map;
    }

    private static SettingChange BoolChange(string key, Func<bool> read, Action<bool> write, bool target) =>
        new()
        {
            Key = key,
            Read = () => read() ? "1" : "0",
            Write = value => write(value == "1"),
            Target = target ? "1" : "0"
        };

    private static async Task<bool> RunOnUiAsync(Func<Task> action)
    {
        try
        {
            return await Ioc.Default.GetRequiredService<IUIThreadDispatcher>().TryRunAsync(action);
        }
        catch
        {
            return false;
        }
    }
}
