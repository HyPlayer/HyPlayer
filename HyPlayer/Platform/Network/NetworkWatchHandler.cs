using System;
using System.Diagnostics;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace HyPlayer.Platform.Network;

internal sealed class NetworkWatchHandler : DelegatingHandler
{
    public static readonly HttpRequestOptionsKey<bool> ProbeKey = new("HyPlayerProbe");

    private readonly Action<TimeSpan, bool> _report;

    public NetworkWatchHandler(HttpMessageHandler inner, Action<TimeSpan, bool> report) : base(inner)
    {
        _report = report;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (request.Options.TryGetValue(ProbeKey, out var probe) && probe)
            return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);

        var started = Stopwatch.GetTimestamp();
        try
        {
            var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
            _report(Stopwatch.GetElapsedTime(started), (int)response.StatusCode >= 500);
            return response;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            _report(Stopwatch.GetElapsedTime(started), true);
            throw;
        }
    }
}
