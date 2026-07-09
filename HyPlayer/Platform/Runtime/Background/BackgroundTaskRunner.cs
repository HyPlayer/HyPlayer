using HyPlayer.Application.Diagnostics;
using HyPlayer.Application.Notifications;
using HyPlayer.Application.State;
using HyPlayer.Features.Account.Services;
using HyPlayer.Features.Downloads.Services;
using HyPlayer.Features.History.Services;
using HyPlayer.Features.LastFM.Services;
using HyPlayer.Features.Lyrics.Services;
using HyPlayer.Features.Playback.QueueProviders;
using HyPlayer.Features.Playback.Services;
using HyPlayer.Features.Widgets.Services;
using HyPlayer.Platform.Runtime;
using HyPlayer.Platform.Runtime.Background;
using HyPlayer.Platform.Storage;
using HyPlayer.Platform.SystemServices;
using HyPlayer.Platform.Tiles;
using HyPlayer.Shell.Navigation.Services;
using HyPlayer.Shell.Playback;
using HyPlayer.Shell.Services;
using HyPlayer.UI.Playback.PlayBar;
using HyPlayer.UI.TeachingTips;
using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Windows.Foundation;

namespace HyPlayer.Platform.Runtime.Background;

/// <inheritdoc />
public sealed class BackgroundTaskRunner : IBackgroundTaskRunner
{
    public void Forget(Task task, string operationName)
    {
        _ = ObserveAsync(task, operationName);
    }

    public void Forget(IAsyncAction action, string operationName)
    {
        if (action == null) return;
        Forget(action.AsTask(), operationName);
    }

    public void Forget(Func<Task> taskFactory, string operationName)
    {
        try
        {
            Forget(taskFactory(), operationName);
        }
        catch (Exception ex)
        {
            Log(operationName, ex);
        }
    }

    private static async Task ObserveAsync(Task task, string operationName)
    {
        try
        {
            await task;
        }
        catch (OperationCanceledException)
        {
            // Expected for playback/load cancellation paths.
        }
        catch (Exception ex)
        {
            Log(operationName, ex);
        }
    }

    private static void Log(string operationName, Exception ex)
    {
        Debug.WriteLine($"Background task '{operationName}' failed: {ex}");
    }
}
