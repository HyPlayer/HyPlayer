using System;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;

namespace HyPlayer.Application.Loading;

public sealed class LoadRequestProbe
{
    private static readonly AsyncLocal<LoadRequestProbe?> Ambient = new();
    private readonly LoadRequestProbe? _parent;
    private Exception? _failure;

    private LoadRequestProbe(LoadRequestProbe? parent)
    {
        _parent = parent;
    }

    public static LoadRequestProbe? Current => Ambient.Value;

    public Exception? Failure => Volatile.Read(ref _failure);

    public static LoadRequestProbe Begin()
    {
        var probe = new LoadRequestProbe(Ambient.Value);
        Ambient.Value = probe;
        return probe;
    }

    public static async Task<T> GuardAsync<T>(Func<Task<T>> load, Func<T, bool> isSuspect)
    {
        ArgumentNullException.ThrowIfNull(load);
        ArgumentNullException.ThrowIfNull(isSuspect);
        var probe = Begin();
        var result = await load();
        if (isSuspect(result))
            probe.ThrowIfFailed();

        return result;
    }

    public void ReportFailure(Exception exception)
    {
        Volatile.Write(ref _failure, exception);
        _parent?.ReportFailure(exception);
    }

    public void ReportSuccess()
    {
        Volatile.Write(ref _failure, null);
        _parent?.ReportSuccess();
    }

    public void ThrowIfFailed()
    {
        switch (Failure)
        {
            case null:
                return;
            case OperationCanceledException canceled:
                throw new TimeoutException(canceled.Message, canceled);
            case { } failure:
                ExceptionDispatchInfo.Capture(failure).Throw();
                return;
        }
    }
}
