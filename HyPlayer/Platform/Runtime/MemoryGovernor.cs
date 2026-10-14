#nullable enable
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Windows.System;
using Windows.UI.Core;
using Windows.UI.Xaml;
using HyPlayer.Platform.Storage.Cache;
using HyPlayer.Platform.Runtime.Background;
using HyPlayer.Features.Playback.Models;
using HyPlayer.Features.Playback.Services;
using HyPlayer.UWP.Chopin.Abstractions.Models;

namespace HyPlayer.Platform.Runtime;

public sealed class MemoryGovernor : IMemoryGovernor, IDisposable, IAsyncDisposable
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
    private static extern unsafe int GlobalMemoryStatusEx(MemoryStatus* status);

    private const ulong MB = 1024 * 1024;
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan Cooldown = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan InactiveDelay = TimeSpan.FromSeconds(60);
    private readonly PlaybackStateService _playbackState;
    private readonly ModelManager _models;
    private readonly AudioGraphPlayer _player;
    private readonly IBackgroundTaskRunner _tasks;
    private readonly Lock _gate = new();
    private readonly IdleResourceRelease _release;
    private Timer? _pollTimer;
    private Window? _window;
    private DateTime _lastRelease = DateTime.MinValue;
    private DateTime? _navigationDue, _inactiveDue;
    private bool _windowInactive, _appInBackground, _expandedOpen, _paused, _disposed;

    public MemoryGovernor(PlaybackStateService playbackState, ModelManager models,
        AudioGraphPlayer player, IBackgroundTaskRunner tasks)
    {
        _playbackState = playbackState; _models = models; _player = player; _tasks = tasks;
        _release = new IdleResourceRelease(() =>
        {
            // ONNX disposal is synchronous and potentially expensive. Only unused sessions
            // are released; the manager checks leases under its own lock.
            _models.ReleaseIdleSessions();
            SimpleCacher.Reset();
        });
    }

    private unsafe ulong WorkingSetBytes
    {
        get
        {
            var counters = new ProcessMemoryCounters { Size = (uint)sizeof(ProcessMemoryCounters) };
            return K32GetProcessMemoryInfo(GetCurrentProcess(), &counters, counters.Size) != 0 ? counters.WorkingSetSize : 0;
        }
    }

    public void Attach(Window window)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!ReferenceEquals(_window, window))
            {
                if (_window is not null) _window.Activated -= OnWindowActivated;
                _window = window;
                _window.Activated += OnWindowActivated;
            }
            ResumePolling();
        }
    }

    private void ResumePolling()
    {
        _paused = false;
        _pollTimer ??= new Timer(_ => Evaluate(), null, PollInterval, PollInterval);
    }

    public void OnEnteredBackground()
    {
        lock (_gate) { _appInBackground = true; RestartInactiveTimer(); }
    }

    public void OnLeavingBackground()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _appInBackground = false;
            if (!_windowInactive) _inactiveDue = null;
            ResumePolling();
        }
    }

    public void ScheduleNavigationReclaim()
    {
        lock (_gate)
            if (!_disposed && !_paused && !_expandedOpen) _navigationDue = DateTime.UtcNow.AddSeconds(2);
    }

    public void OnExpandedPlayerVisibilityChanged(bool open)
    {
        lock (_gate)
        {
            _expandedOpen = open;
            _navigationDue = open ? null : DateTime.UtcNow.AddSeconds(2);
        }
    }

    private void OnWindowActivated(object sender, WindowActivatedEventArgs e)
    {
        lock (_gate)
        {
            _windowInactive = e.WindowActivationState == CoreWindowActivationState.Deactivated;
            if (_windowInactive) RestartInactiveTimer();
            else if (!_appInBackground) _inactiveDue = null;
        }
    }

    private void RestartInactiveTimer()
    {
        _inactiveDue = DateTime.UtcNow + InactiveDelay;
    }

    private void Evaluate()
    {
        try
        {
            lock (_gate)
            {
                if (_disposed || _paused || _models.IsBusy || _player.RealtimeCallbacksActive || _playbackState.IsTransitioning) return;
                var now = DateTime.UtcNow;
                if (now - _lastRelease < Cooldown) return;
                var navigation = _navigationDue is { } nav && now >= nav;
                var inactive = (_windowInactive || _appInBackground) && _inactiveDue is { } due && now >= due;
                if (!navigation && !inactive && WorkingSetBytes <= Threshold()) return;
                _navigationDue = null; _inactiveDue = null; _lastRelease = now;
                _tasks.Forget(_release.RunAsync(), "release idle resources");
            }
        }
        catch (Exception ex) { Debug.WriteLine($"Idle resource evaluation failed: {ex}"); }
    }

    private unsafe ulong Threshold()
    {
        var status = new MemoryStatus { Length = (uint)sizeof(MemoryStatus) };
        var known = GlobalMemoryStatusEx(&status) != 0 && status.TotalPhys > 0;
        var tight = MemoryManager.AppMemoryUsageLevel >= AppMemoryUsageLevel.High ||
            known && (status.AvailPhys < status.TotalPhys / 8 || status.AvailPhys < 1536 * MB);
        if (tight) return 384 * MB;
        if (_windowInactive || _appInBackground) return 768 * MB;
        return Math.Max(2048 * MB, (known ? status.TotalPhys : 8192 * MB) / 5);
    }

    public async Task PauseAsync()
    {
        Timer? timer;
        lock (_gate)
        {
            _paused = true; _navigationDue = null; _inactiveDue = null;
            timer = _pollTimer; _pollTimer = null;
        }
        if (timer is not null) await timer.DisposeAsync().ConfigureAwait(false);
        await _release.DrainAsync().ConfigureAwait(false);
    }

    public void Resume()
    {
        lock (_gate)
        {
            if (_disposed) return;
            ResumePolling();
            if (_windowInactive || _appInBackground) RestartInactiveTimer();
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true; _paused = true;
            _pollTimer?.Dispose(); _pollTimer = null;
            if (_window is not null) _window.Activated -= OnWindowActivated;
            _window = null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        try { await PauseAsync().ConfigureAwait(false); }
        finally { Dispose(); }
    }
}
