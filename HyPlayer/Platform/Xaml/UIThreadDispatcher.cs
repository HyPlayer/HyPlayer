using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Windows.ApplicationModel.Core;
using Windows.UI.Core;
using HyPlayer.Application.State;
using HyPlayer.Application.Threading;

namespace HyPlayer.Platform.Xaml;

/// <inheritdoc />
public sealed class UIThreadDispatcher : IUIThreadDispatcher
{
    private readonly IAppLifecycleStateService _lifecycle;

    public UIThreadDispatcher(IAppLifecycleStateService lifecycle)
    {
        _lifecycle = lifecycle;
    }

    public Task<bool> TryRunAsync(Action action)
    {
        return TryRunCoreAsync(action);
    }

    public Task<bool> TryRunAsync(Func<Task> action)
    {
        return TryRunCoreAsync(action);
    }

    private async Task<bool> TryRunCoreAsync(Action action)
    {
        var dispatcher = TryGetMainDispatcher();
        if (dispatcher is null) return false;

        await dispatcher.RunOnUIThreadAsync(action);
        return true;
    }

    private async Task<bool> TryRunCoreAsync(Func<Task> action)
    {
        var dispatcher = TryGetMainDispatcher();
        if (dispatcher is null) return false;

        await dispatcher.RunOnUIThreadAsync(action);
        return true;
    }

    private CoreDispatcher? TryGetMainDispatcher()
    {
        if (_lifecycle.IsInBackground) return null;

        try
        {
            return CoreApplication.Views.Count > 0
                ? CoreApplication.MainView.Dispatcher
                : null;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"UI dispatch failed: {ex.Message}");
            return null;
        }
    }
}