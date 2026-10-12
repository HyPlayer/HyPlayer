#nullable enable
using System;
using System.Runtime;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Windows.System;
using Windows.UI.Core;
using Windows.UI.Xaml;
using HyPlayer.Domain.Settings;
using HyPlayer.Features.Playback.Models;
using HyPlayer.Features.Playback.Services;
using HyPlayer.UWP.Chopin.Abstractions.Models;

namespace HyPlayer.Platform.Runtime;

internal enum DeferredReclaim
{
    Wait,
    Run,
    Drop
}

internal static class ReclaimPolicy
{
    public const int SettledPolls = 2;

    public static bool IsFull(bool manual, bool playing) => manual || !playing;

    public static bool AllowsBackgroundCollect(bool realtimeCallbacks, bool transitioning) => !realtimeCallbacks && !transitioning;

    public static DeferredReclaim Deferred(bool pending, int pollsWithoutPlayback, bool busy, bool inactive, bool overLimit)
    {
        if (!pending || pollsWithoutPlayback < SettledPolls || busy) return DeferredReclaim.Wait;
        return inactive || overLimit ? DeferredReclaim.Run : DeferredReclaim.Drop;
    }
}

internal sealed class NavigationReclaimGate
{
    private int _version;
    private volatile bool _expandedPlayerOpen;

    public bool AllowsNavigationReclaim => !_expandedPlayerOpen;

    public int Begin() => Interlocked.Increment(ref _version);

    public bool IsCurrent(int ticket) => ticket == Volatile.Read(ref _version);

    public void SetExpandedPlayerOpen(bool open)
    {
        _expandedPlayerOpen = open;
        Interlocked.Increment(ref _version);
    }
}

public sealed class MemoryGovernor : IMemoryGovernor
{
    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessMemoryCounters
    {
        public uint Size;
        public uint PageFaultCount;
        public nuint PeakWorkingSetSize;
        public nuint WorkingSetSize;
        public nuint QuotaPeakPagedPoolUsage;
        public nuint QuotaPagedPoolUsage;
        public nuint QuotaPeakNonPagedPoolUsage;
        public nuint QuotaNonPagedPoolUsage;
        public nuint PagefileUsage;
        public nuint PeakPagefileUsage;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatus
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhys;
        public ulong AvailPhys;
        public ulong TotalPageFile;
        public ulong AvailPageFile;
        public ulong TotalVirtual;
        public ulong AvailVirtual;
        public ulong AvailExtendedVirtual;
    }

    [DllImport("kernel32.dll")]
    private static extern nint GetCurrentProcess();

    [DllImport("kernel32.dll")]
    private static extern unsafe int K32GetProcessMemoryInfo(nint process, ProcessMemoryCounters* counters, uint size);

    [DllImport("kernel32.dll")]
    private static extern int K32EmptyWorkingSet(nint process);

    [DllImport("kernel32.dll")]
    private static extern unsafe int GlobalMemoryStatusEx(MemoryStatus* status);

    private const ulong MB = 1024 * 1024;
    private const ulong RegrowthBeforeRetry = 256 * MB;
    private const ulong IneffectiveReclaim = 64 * MB;
    private static readonly TimeSpan NavigationReclaimDelay = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan BaseCooldown = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan MaxCooldown = TimeSpan.FromMinutes(15);

    private readonly UISettings _settings;
    private readonly PlaybackStateService _playbackState;
    private readonly ModelManager _models;
    private readonly AudioGraphPlayer _player;

    private Timer? _pollTimer;
    private CancellationTokenSource? _inactiveCts;
    private Window? _window;
    private int _reclaiming;
    private DateTime _lastReclaim = DateTime.MinValue;
    private TimeSpan _cooldown = BaseCooldown;
    private ulong _workingSetAfterReclaim;
    private volatile bool _windowInactive;
    private volatile bool _appInBackground;
    private volatile bool _fullReclaimPending;
    private int _pollsWithoutPlayback;
    private readonly NavigationReclaimGate _navigationReclaim = new();

    public MemoryGovernor(
        UISettings settings,
        PlaybackStateService playbackState,
        ModelManager models,
        AudioGraphPlayer player)
    {
        _settings = settings;
        _playbackState = playbackState;
        _models = models;
        _player = player;
    }

    public unsafe ulong WorkingSetBytes
    {
        get
        {
            var counters = new ProcessMemoryCounters { Size = (uint)sizeof(ProcessMemoryCounters) };
            return K32GetProcessMemoryInfo(GetCurrentProcess(), &counters, counters.Size) != 0
                ? counters.WorkingSetSize
                : 0;
        }
    }

    private bool IsInactive => _windowInactive || _appInBackground;

    public void Attach(Window window)
    {
        if (ReferenceEquals(_window, window)) return;
        if (_window is not null) _window.Activated -= OnWindowActivated;
        _window = window;
        window.Activated += OnWindowActivated;
        _pollTimer ??= new Timer(_ => Evaluate(), null, PollInterval, PollInterval);
    }

    public void OnEnteredBackground()
    {
        _appInBackground = true;
        _navigationReclaim.Begin();
        _ = Task.Run(Collect);
        RestartInactiveTimer();
    }

    public void OnLeavingBackground()
    {
        _appInBackground = false;
        if (!_windowInactive) CancelInactiveTimer();
    }

    public void ScheduleNavigationReclaim()
    {
        if (_navigationReclaim.AllowsNavigationReclaim) ScheduleCollect();
    }

    public void OnExpandedPlayerVisibilityChanged(bool open)
    {
        _navigationReclaim.SetExpandedPlayerOpen(open);
        if (!open) ScheduleCollect();
    }

    private void ScheduleCollect()
    {
        var ticket = _navigationReclaim.Begin();
        _ = Task.Run(async () =>
        {
            await Task.Delay(NavigationReclaimDelay);
            if (!_navigationReclaim.IsCurrent(ticket)) return;
            Collect();
        });
    }

    private void Collect()
    {
        if (!ReclaimPolicy.AllowsBackgroundCollect(_player.RealtimeCallbacksActive, _playbackState.IsTransitioning))
        {
            ScheduleCollect();
            return;
        }

        GC.Collect(2, GCCollectionMode.Forced, blocking: false);
    }

    public Task<ulong> ReclaimAsync() => Task.Run(() => Reclaim(true));

    private void OnWindowActivated(object sender, WindowActivatedEventArgs e)
    {
        _windowInactive = e.WindowActivationState == CoreWindowActivationState.Deactivated;
        if (_windowInactive) RestartInactiveTimer();
        else if (!_appInBackground) CancelInactiveTimer();
    }

    private void CancelInactiveTimer()
    {
        Interlocked.Exchange(ref _inactiveCts, null)?.Cancel();
    }

    private void RestartInactiveTimer()
    {
        var seconds = _settings.BackgroundReleaseSeconds;
        var cts = new CancellationTokenSource();
        Interlocked.Exchange(ref _inactiveCts, cts)?.Cancel();
        if (seconds <= 0) return;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(seconds), cts.Token);
                while (IsBusy()) await Task.Delay(TimeSpan.FromSeconds(10), cts.Token);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            if (IsInactive && CooldownElapsed()) Reclaim(false);
        });
    }

    private bool CooldownElapsed() => DateTime.UtcNow - _lastReclaim >= _cooldown;

    private bool IsBusy()
    {
        try
        {
            return _models.IsBusy || _player.RealtimeCallbacksActive || _playbackState.IsTransitioning;
        }
        catch
        {
            return false;
        }
    }

    private void Evaluate()
    {
        try
        {
            _pollsWithoutPlayback = _playbackState.IsPlaying ? 0 : Math.Min(_pollsWithoutPlayback + 1, ReclaimPolicy.SettledPolls);
            var limitMb = _settings.MemoryLimitMB;
            var busy = IsBusy();
            if (_fullReclaimPending)
            {
                var overLimit = limitMb != 0 && WorkingSetBytes > Threshold(limitMb);
                switch (ReclaimPolicy.Deferred(true, _pollsWithoutPlayback, busy, IsInactive, overLimit))
                {
                    case DeferredReclaim.Run:
                        Reclaim(false);
                        return;
                    case DeferredReclaim.Drop:
                        _fullReclaimPending = false;
                        break;
                }
            }

            if (limitMb == 0 || !CooldownElapsed() || busy) return;
            var workingSet = WorkingSetBytes;
            if (workingSet < _workingSetAfterReclaim + RegrowthBeforeRetry) return;
            if (workingSet > Threshold(limitMb)) Reclaim(false);
        }
        catch
        {
        }
    }

    private ulong Threshold(int limitMb) => limitMb > 0 ? (ulong)limitMb * MB : AutoThreshold();

    private unsafe ulong AutoThreshold()
    {
        var status = new MemoryStatus { Length = (uint)sizeof(MemoryStatus) };
        var known = GlobalMemoryStatusEx(&status) != 0 && status.TotalPhys > 0;
        var systemTight = MemoryManager.AppMemoryUsageLevel >= AppMemoryUsageLevel.High ||
                          known && (status.AvailPhys < status.TotalPhys / 8 || status.AvailPhys < 1536 * MB);
        if (systemTight) return 384 * MB;
        if (IsInactive) return 768 * MB;
        var total = known ? status.TotalPhys : 8192 * MB;
        return Math.Max(2048 * MB, total / 5);
    }

    private ulong Reclaim(bool manual)
    {
        if (Interlocked.Exchange(ref _reclaiming, 1) == 1) return 0;
        var before = WorkingSetBytes;
        ulong freed = 0;
        var full = ReclaimPolicy.IsFull(manual, _playbackState.IsPlaying);
        try
        {
            try
            {
                _models.ReleaseIdleSessions();
            }
            catch
            {
            }

            if (full)
            {
                GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
                GC.Collect(2, GCCollectionMode.Aggressive, blocking: true, compacting: true);
                GC.WaitForPendingFinalizers();
                K32EmptyWorkingSet(GetCurrentProcess());
            }
            else if (ReclaimPolicy.AllowsBackgroundCollect(_player.RealtimeCallbacksActive, _playbackState.IsTransitioning))
            {
                GC.Collect(2, GCCollectionMode.Forced, blocking: false);
            }
        }
        catch
        {
        }
        finally
        {
            var after = WorkingSetBytes;
            freed = before > after ? before - after : 0;
            _cooldown = freed < IneffectiveReclaim
                ? TimeSpan.FromTicks(Math.Min(_cooldown.Ticks * 2, MaxCooldown.Ticks))
                : BaseCooldown;
            _workingSetAfterReclaim = after;
            _lastReclaim = DateTime.UtcNow;
            _fullReclaimPending = !full;
            Volatile.Write(ref _reclaiming, 0);
        }

        return freed;
    }
}
