using System;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;

namespace HyPlayer.Application.Loading;

public enum LoadPhase
{
    Idle,
    Loading,
    Loaded,
    Empty,
    Failed
}

public sealed partial class LoadState : ObservableObject
{
    public const string SlowText = "网络较慢，仍在加载…";
    public const string WaitingForLoginText = "正在等待登录完成…";
    public const string SlowLoginText = "正在连接至网易云，网络较慢时可能需要一些时间";
    public const string SignedOutText = "尚未登录";
    public const string DefaultEmptyText = "这里还没有内容";

    public static readonly TimeSpan DefaultSlowDelay = TimeSpan.FromSeconds(4);
    private static readonly TimeSpan LoginPollInterval = TimeSpan.FromMilliseconds(400);

    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly ILoadEnvironment? _environment;
    private int _attempt;
    private CancellationTokenSource? _attemptCancellation;
    private string? _emptyOverride;
    private Func<Task>? _retry;
    private Task? _running;

    public LoadState(
        string subject = "",
        ILoadEnvironment? environment = null,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        Subject = subject;
        _environment = environment;
        _delay = delay ?? Task.Delay;
    }

    public string Subject { get; set; }
    public string? LoadingText { get; set; }
    public string EmptyText { get; set; } = DefaultEmptyText;
    public bool RequiresAccount { get; set; }
    public TimeSpan SlowDelay { get; set; } = DefaultSlowDelay;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLoading))]
    [NotifyPropertyChangedFor(nameof(IsLoaded))]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    [NotifyPropertyChangedFor(nameof(IsFailed))]
    [NotifyPropertyChangedFor(nameof(IsVisible))]
    [NotifyPropertyChangedFor(nameof(CanRetry))]
    [NotifyPropertyChangedFor(nameof(Title))]
    [NotifyPropertyChangedFor(nameof(Detail))]
    public partial LoadPhase Phase { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Detail))]
    public partial bool IsSlow { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Title))]
    [NotifyPropertyChangedFor(nameof(Detail))]
    public partial bool IsWaitingForLogin { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRetry))]
    [NotifyPropertyChangedFor(nameof(Title))]
    [NotifyPropertyChangedFor(nameof(Detail))]
    public partial LoadFailure? Failure { get; private set; }

    public bool IsLoading => Phase == LoadPhase.Loading;
    public bool IsLoaded => Phase == LoadPhase.Loaded;
    public bool IsEmpty => Phase == LoadPhase.Empty;
    public bool IsFailed => Phase == LoadPhase.Failed;
    public bool IsVisible => Phase is LoadPhase.Loading or LoadPhase.Empty or LoadPhase.Failed;
    public bool CanRetry => Phase == LoadPhase.Failed && Failure is { CanRetry: true } && _retry is not null;

    public string Title => Phase switch
    {
        LoadPhase.Loading when IsWaitingForLogin => WaitingForLoginText,
        LoadPhase.Loading => LoadingText ?? $"正在加载{Subject}…",
        LoadPhase.Empty => _emptyOverride ?? EmptyText,
        LoadPhase.Failed when Failure?.Kind == LoadFailureKind.NotLoggedIn => SignedOutText,
        LoadPhase.Failed => $"{Subject}加载失败",
        _ => string.Empty
    };

    public string Detail => Phase switch
    {
        LoadPhase.Loading when IsWaitingForLogin => IsSlow ? SlowLoginText : $"登录后将自动加载{Subject}",
        LoadPhase.Loading when IsSlow => SlowText,
        LoadPhase.Failed when Failure?.Kind == LoadFailureKind.NotLoggedIn => $"登录后可查看{Subject}",
        LoadPhase.Failed => Failure?.Reason ?? string.Empty,
        _ => string.Empty
    };

    public Task RunAsync<T>(
        Func<CancellationToken, Task<T>> load,
        Action<T> apply,
        Func<T, bool>? isEmpty = null,
        Func<Task>? retry = null)
    {
        ArgumentNullException.ThrowIfNull(load);
        ArgumentNullException.ThrowIfNull(apply);
        var attempt = Begin(retry ?? (() => RunAsync(load, apply, isEmpty)), out var cancellationToken);
        var running = RunCoreAsync(attempt, cancellationToken, load, apply, isEmpty);
        if (attempt == _attempt)
            _running = running;
        return running;
    }

    public Task RunAsync(Func<CancellationToken, Task<bool>> load)
    {
        ArgumentNullException.ThrowIfNull(load);
        return RunAsync(load, static _ => { }, static hasContent => !hasContent);
    }

    public Task RetryAsync()
    {
        if (Phase == LoadPhase.Loading)
            return _running ?? Task.CompletedTask;

        return Phase == LoadPhase.Failed && _retry is { } retry ? retry() : Task.CompletedTask;
    }

    public int Begin(Func<Task>? retry, out CancellationToken cancellationToken)
    {
        ReleaseAttempt();
        var attempt = ++_attempt;
        var cancellation = new CancellationTokenSource();
        _attemptCancellation = cancellation;
        cancellationToken = cancellation.Token;
        _retry = retry;
        _running = null;
        _emptyOverride = null;
        Failure = null;
        IsSlow = false;
        IsWaitingForLogin = false;
        Phase = LoadPhase.Loading;
        _ = WatchSlowAsync(attempt, cancellationToken);
        return attempt;
    }

    public bool IsCurrent(int attempt)
    {
        return attempt == _attempt;
    }

    public void Complete(int attempt, bool hasContent)
    {
        if (attempt != _attempt || Phase != LoadPhase.Loading)
            return;

        ReleaseAttempt();
        IsSlow = false;
        IsWaitingForLogin = false;
        Phase = hasContent ? LoadPhase.Loaded : LoadPhase.Empty;
    }

    public void Fail(int attempt, Exception exception)
    {
        if (attempt != _attempt || Phase != LoadPhase.Loading)
            return;

        var cancellationToken = _attemptCancellation?.Token ?? CancellationToken.None;
        Fail(attempt, LoadFailureClassifier.Classify(exception, cancellationToken,
            _environment?.IsNetworkAvailable ?? true));
    }

    public void Fail(int attempt, LoadFailure failure)
    {
        if (attempt != _attempt || Phase != LoadPhase.Loading)
            return;

        ReleaseAttempt();
        IsSlow = false;
        IsWaitingForLogin = false;
        if (failure.IsSilent)
        {
            Phase = LoadPhase.Idle;
            return;
        }

        Failure = failure;
        Phase = LoadPhase.Failed;
        if (failure.Detail.Length > 0)
            _environment?.Record(Subject, failure);
    }

    public void Cancel()
    {
        _attempt++;
        ReleaseAttempt();
        _retry = null;
        _running = null;
        _emptyOverride = null;
        Failure = null;
        IsSlow = false;
        IsWaitingForLogin = false;
        Phase = LoadPhase.Idle;
    }

    public void ShowEmpty(string? text = null)
    {
        Cancel();
        _emptyOverride = text;
        Phase = LoadPhase.Empty;
    }

    public void ShowLoaded()
    {
        Cancel();
        Phase = LoadPhase.Loaded;
    }

    private async Task RunCoreAsync<T>(
        int attempt,
        CancellationToken cancellationToken,
        Func<CancellationToken, Task<T>> load,
        Action<T> apply,
        Func<T, bool>? isEmpty)
    {
        try
        {
            if (RequiresAccount && _environment is { } environment)
            {
                var waited = false;
                while (environment.IsLoginPending)
                {
                    waited = true;
                    IsWaitingForLogin = true;
                    await _delay(LoginPollInterval, cancellationToken);
                    if (attempt != _attempt)
                        return;
                }

                if (!environment.IsLoggedIn)
                {
                    Fail(attempt, new LoadFailure(LoadFailureKind.NotLoggedIn,
                        LoadFailureClassifier.NotLoggedInReason, string.Empty));
                    return;
                }

                if (waited)
                {
                    IsWaitingForLogin = false;
                    if (IsSlow)
                    {
                        IsSlow = false;
                        _ = WatchSlowAsync(attempt, cancellationToken);
                    }
                }
            }

            var probe = LoadRequestProbe.Begin();
            var result = await load(cancellationToken);
            if (attempt != _attempt)
                return;

            var empty = isEmpty?.Invoke(result) ?? false;
            if (empty && probe.Failure is { } swallowed)
            {
                Fail(attempt, swallowed);
                return;
            }

            apply(result);
            Complete(attempt, !empty);
        }
        catch (Exception exception)
        {
            Fail(attempt, exception);
        }
    }

    private async Task WatchSlowAsync(int attempt, CancellationToken cancellationToken)
    {
        try
        {
            await _delay(SlowDelay, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (attempt == _attempt && Phase == LoadPhase.Loading)
            IsSlow = true;
    }

    private void ReleaseAttempt()
    {
        if (_attemptCancellation is not { } cancellation)
            return;

        _attemptCancellation = null;
        cancellation.Cancel();
    }
}
