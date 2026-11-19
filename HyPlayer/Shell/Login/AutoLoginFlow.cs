using System;
using System.Threading;
using System.Threading.Tasks;
using HyPlayer.Features.Account.Services;

namespace HyPlayer.Shell.Login;

public enum AutoLoginState
{
    None,
    Pending,
    Unreachable
}

public enum AutoLoginTrigger
{
    Startup,
    Manual,
    Automatic
}

public sealed class AutoLoginFlow
{
    public static readonly TimeSpan SlowNoticeDelay = TimeSpan.FromSeconds(6);
    public static readonly TimeSpan AttemptTimeout = TimeSpan.FromSeconds(60);
    public static readonly TimeSpan NetworkSettleDelay = TimeSpan.FromSeconds(2);

    private static readonly TimeSpan[] RetryDelays =
    [
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(15),
        TimeSpan.FromSeconds(30),
        TimeSpan.FromSeconds(60),
        TimeSpan.FromSeconds(120),
        TimeSpan.FromSeconds(300)
    ];

    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly Func<CancellationToken, Task<SavedLoginResult>> _restore;

    private CancellationTokenSource? _attemptCancellation;
    private bool _attemptRunning;
    private int _failedAttempts;
    private int _generation;
    private bool _networkChanged;
    private bool _slowNotified;
    private CancellationTokenSource? _waitCancellation;

    public AutoLoginFlow(
        Func<CancellationToken, Task<SavedLoginResult>> restore,
        Func<TimeSpan, CancellationToken, Task> delay)
    {
        _restore = restore;
        _delay = delay;
    }

    public AutoLoginState State { get; private set; }

    public event Action<AutoLoginState>? StateChanged;
    public event Action? SlowDetected;
    public event Action<SavedLoginResult, AutoLoginTrigger>? AttemptCompleted;

    public static TimeSpan GetRetryDelay(int failedAttempts)
    {
        return RetryDelays[Math.Clamp(failedAttempts - 1, 0, RetryDelays.Length - 1)];
    }

    public Task StartAsync()
    {
        return State == AutoLoginState.None ? RunAttemptAsync(AutoLoginTrigger.Startup) : Task.CompletedTask;
    }

    public Task RetryAsync()
    {
        return State == AutoLoginState.Unreachable ? RunAttemptAsync(AutoLoginTrigger.Manual) : Task.CompletedTask;
    }

    public void NotifyNetworkAvailable()
    {
        if (_attemptRunning)
        {
            _networkChanged = true;
            return;
        }

        if (State == AutoLoginState.Unreachable)
            ScheduleRetry(NetworkSettleDelay);
    }

    public void Stop()
    {
        _generation++;
        _failedAttempts = 0;
        CancelWait();
        _attemptCancellation?.Cancel();
        SetState(AutoLoginState.None);
    }

    private async Task RunAttemptAsync(AutoLoginTrigger trigger)
    {
        if (_attemptRunning) return;

        _attemptRunning = true;
        CancelWait();
        _networkChanged = false;
        var generation = ++_generation;
        var attempt = new CancellationTokenSource();
        var watch = new CancellationTokenSource();
        _attemptCancellation = attempt;
        SetState(AutoLoginState.Pending);
        _ = WatchAttemptAsync(attempt, watch.Token);

        SavedLoginResult result;
        try
        {
            result = await _restore(attempt.Token);
        }
        catch (Exception ex)
        {
            result = new SavedLoginResult(SavedLoginStatus.Unreachable, ex.Message);
        }

        watch.Cancel();
        _attemptCancellation = null;
        _attemptRunning = false;
        if (generation != _generation) return;

        if (result.Status != SavedLoginStatus.Unreachable)
        {
            _failedAttempts = 0;
            SetState(AutoLoginState.None);
            AttemptCompleted?.Invoke(result, trigger);
            return;
        }

        _failedAttempts++;
        SetState(AutoLoginState.Unreachable);
        AttemptCompleted?.Invoke(result, trigger);
        if (generation == _generation && State == AutoLoginState.Unreachable && _waitCancellation is null)
            ScheduleRetry(_networkChanged ? NetworkSettleDelay : GetRetryDelay(_failedAttempts));
    }

    private async Task WatchAttemptAsync(CancellationTokenSource attempt, CancellationToken watch)
    {
        try
        {
            var remaining = AttemptTimeout;
            if (!_slowNotified)
            {
                await _delay(SlowNoticeDelay, watch);
                if (watch.IsCancellationRequested) return;

                remaining -= SlowNoticeDelay;
                if (!_slowNotified)
                {
                    _slowNotified = true;
                    SlowDetected?.Invoke();
                }
            }

            await _delay(remaining, watch);
            if (watch.IsCancellationRequested) return;

            attempt.Cancel();
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void ScheduleRetry(TimeSpan delay)
    {
        CancelWait();
        var wait = new CancellationTokenSource();
        _waitCancellation = wait;
        _ = WaitAndRetryAsync(delay, wait);
    }

    private async Task WaitAndRetryAsync(TimeSpan delay, CancellationTokenSource wait)
    {
        try
        {
            await _delay(delay, wait.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (wait.IsCancellationRequested || !ReferenceEquals(_waitCancellation, wait)) return;

        _waitCancellation = null;
        if (State == AutoLoginState.Unreachable)
            await RunAttemptAsync(AutoLoginTrigger.Automatic);
    }

    private void CancelWait()
    {
        var wait = _waitCancellation;
        _waitCancellation = null;
        wait?.Cancel();
    }

    private void SetState(AutoLoginState state)
    {
        if (State == state) return;

        State = state;
        StateChanged?.Invoke(state);
    }
}
