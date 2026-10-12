using System;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;
using HyPlayer.NeteaseApi.Bases;

namespace HyPlayer.Application.Loading;

public enum LoadFailureKind
{
    Cancelled,
    Offline,
    Timeout,
    Server,
    Rejected,
    NotLoggedIn,
    Unknown
}

public sealed record LoadFailure(LoadFailureKind Kind, string Reason, string Detail)
{
    public bool IsSilent => Kind == LoadFailureKind.Cancelled;
    public bool CanRetry => Kind is not (LoadFailureKind.Cancelled or LoadFailureKind.NotLoggedIn);
}

public static class LoadFailureClassifier
{
    public const string OfflineReason = "无法连接到网络，请检查网络连接";
    public const string TimeoutReason = "连接超时，网络可能较慢";
    public const string ServerReason = "服务器暂时无法响应";
    public const string RejectedReason = "服务器拒绝了请求";
    public const string NotLoggedInReason = "需要登录后才能查看";
    public const string UnknownReason = "出现了一些问题，请稍后重试";

    private const int MaxDepth = 8;

    public static LoadFailure Classify(
        Exception exception,
        CancellationToken cancellationToken = default,
        bool networkAvailable = true)
    {
        ArgumentNullException.ThrowIfNull(exception);
        var detail = Describe(exception);
        var (kind, code) = Resolve(exception, cancellationToken, 0);
        if (!networkAvailable && kind is LoadFailureKind.Timeout or LoadFailureKind.Unknown)
            kind = LoadFailureKind.Offline;

        return new LoadFailure(kind, ReasonOf(kind, code), detail);
    }

    public static string ReasonOf(LoadFailureKind kind, int? code = null)
    {
        return kind switch
        {
            LoadFailureKind.Cancelled => string.Empty,
            LoadFailureKind.Offline => OfflineReason,
            LoadFailureKind.Timeout => TimeoutReason,
            LoadFailureKind.Server => code is { } serverCode ? $"{ServerReason}（{serverCode}）" : ServerReason,
            LoadFailureKind.Rejected => code is { } rejectedCode ? $"{RejectedReason}（{rejectedCode}）" : RejectedReason,
            LoadFailureKind.NotLoggedIn => NotLoggedInReason,
            _ => UnknownReason
        };
    }

    private static (LoadFailureKind Kind, int? Code) Resolve(
        Exception exception,
        CancellationToken cancellationToken,
        int depth)
    {
        switch (exception)
        {
            case AggregateException aggregate when aggregate.InnerExceptions.Count > 0 && depth < MaxDepth:
                return Resolve(aggregate.Flatten().InnerExceptions[0], cancellationToken, depth + 1);
            case OperationCanceledException canceled:
                if (HasTimeoutCause(canceled))
                    return (LoadFailureKind.Timeout, null);
                if (cancellationToken.IsCancellationRequested || !cancellationToken.CanBeCanceled)
                    return (LoadFailureKind.Cancelled, null);
                return (LoadFailureKind.Timeout, null);
            case TimeoutException:
                return (LoadFailureKind.Timeout, null);
            case ExceptionedErrorBase { InnerException: { } wrapped } when depth < MaxDepth:
                return Resolve(wrapped, cancellationToken, depth + 1);
            case ErrorResultBase provider:
                return FromCode(provider.ErrorCode);
            case HttpRequestException http:
                return FromHttp(http, cancellationToken, depth);
            case SocketException socket:
                return (socket.SocketErrorCode == SocketError.TimedOut ? LoadFailureKind.Timeout : LoadFailureKind.Offline,
                    null);
            case WebException web:
                return (web.Status == WebExceptionStatus.Timeout ? LoadFailureKind.Timeout : LoadFailureKind.Offline,
                    null);
        }

        if (FromHResult(exception.HResult) is { } known)
            return (known, null);

        if (exception.InnerException is { } inner && depth < MaxDepth)
        {
            var resolved = Resolve(inner, cancellationToken, depth + 1);
            if (resolved.Kind != LoadFailureKind.Unknown)
                return resolved;
        }

        return (LoadFailureKind.Unknown, null);
    }

    private static bool HasTimeoutCause(Exception exception)
    {
        for (var inner = exception.InnerException; inner is not null; inner = inner.InnerException)
            if (inner is TimeoutException)
                return true;

        return false;
    }

    private static (LoadFailureKind Kind, int? Code) FromHttp(
        HttpRequestException exception,
        CancellationToken cancellationToken,
        int depth)
    {
        if (exception.StatusCode is { } status)
            return FromCode((int)status);

        if (exception.InnerException is { } inner && depth < MaxDepth)
        {
            var resolved = Resolve(inner, cancellationToken, depth + 1);
            if (resolved.Kind is LoadFailureKind.Timeout or LoadFailureKind.Cancelled)
                return resolved;
        }

        return (LoadFailureKind.Offline, null);
    }

    private static (LoadFailureKind Kind, int? Code) FromCode(int code)
    {
        return code switch
        {
            < 0 => (LoadFailureKind.Unknown, null),
            301 or 401 => (LoadFailureKind.NotLoggedIn, null),
            408 or 504 => (LoadFailureKind.Timeout, null),
            >= 500 and < 600 => (LoadFailureKind.Server, code),
            _ => (LoadFailureKind.Rejected, code)
        };
    }

    private static LoadFailureKind? FromHResult(int hresult)
    {
        return unchecked((uint)hresult) switch
        {
            0x80072EE7 or 0x80072EFD or 0x80072EFE or 0x80072F8F or 0x80072EE6 => LoadFailureKind.Offline,
            0x80072EE2 => LoadFailureKind.Timeout,
            _ => null
        };
    }

    private static string Describe(Exception exception)
    {
        var text = $"{exception.GetType().Name}: {exception.Message}";
        var depth = 0;
        for (var inner = InnerOf(exception); inner is not null && depth < MaxDepth; inner = InnerOf(inner), depth++)
            text += $" <- {inner.GetType().Name}: {inner.Message}";

        return text;
    }

    private static Exception? InnerOf(Exception exception)
    {
        return exception is ExceptionedErrorBase { InnerException: { } wrapped } ? wrapped : exception.InnerException;
    }
}
