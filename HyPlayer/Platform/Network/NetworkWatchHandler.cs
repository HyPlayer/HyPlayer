using System;
using System.Diagnostics;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using HyPlayer.Application.Loading;

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

        var loadProbe = LoadRequestProbe.Current;
        var started = Stopwatch.GetTimestamp();
        try
        {
            var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
            _report(Stopwatch.GetElapsedTime(started), (int)response.StatusCode >= 500);
            NetworkAnswerProbe.Report(response.IsSuccessStatusCode);
            if (response.IsSuccessStatusCode)
                loadProbe?.ReportSuccess();
            else
                loadProbe?.ReportFailure(new HttpRequestException(
                    $"HTTP {(int)response.StatusCode}", null, response.StatusCode));
            return response;
        }
        catch (OperationCanceledException exception) when (cancellationToken.IsCancellationRequested)
        {
            loadProbe?.ReportFailure(exception);
            throw;
        }
        catch (Exception exception)
        {
            _report(Stopwatch.GetElapsedTime(started), true);
            loadProbe?.ReportFailure(exception);
            throw;
        }
    }
}
