using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Media.Animation;

namespace HyPlayer.Shell.Playback;

/// <summary>
/// Keeps prepared snapshots alive until their animations finish or are explicitly cancelled.
/// All methods and continuations run on the owning XAML UI thread.
/// </summary>
internal sealed class ConnectedAnimationBatch
{
    private readonly Dictionary<ConnectedAnimation, FrameworkElement> _pending = new();
    private readonly TaskCompletionSource<bool> _completion = new();
    private bool _starting;

    public void Prepare(string key, FrameworkElement source, FrameworkElement target)
    {
        if (!source.IsLoaded || source.ActualWidth <= 0 || source.ActualHeight <= 0)
            return;

        try
        {
            var animation = ConnectedAnimationService.GetForCurrentView().PrepareToAnimate(key, source);
            _pending.Add(animation, target);
            animation.Completed += OnCompleted;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Could not prepare playback connected animation {key}: {ex}");
        }
    }

    public async Task StartAsync()
    {
        if (_completion.Task.IsCompleted) return;
        _starting = true;
        foreach (var (animation, target) in _pending.ToArray())
        {
            try
            {
                animation.Configuration = new DirectConnectedAnimationConfiguration();
                if (target.IsLoaded && target.Visibility == Visibility.Visible &&
                    target.ActualWidth > 0 && target.ActualHeight > 0 && animation.TryStart(target))
                    continue;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Could not start playback connected animation: {ex}");
            }

            Remove(animation, cancel: true);
        }
        _starting = false;
        if (_pending.Count == 0) _completion.TrySetResult(true);

        // Completed may not arrive when a window is hidden/suspended. Cancel before releasing
        // the source tree; a delay alone is not evidence that DWM consumed a snapshot.
        await Task.WhenAny(_completion.Task, Task.Delay(TimeSpan.FromSeconds(1)));
        Cancel();
    }

    public void Cancel()
    {
        foreach (var animation in _pending.Keys.ToArray())
            Remove(animation, cancel: true);
        _completion.TrySetResult(true);
    }

    private void OnCompleted(ConnectedAnimation sender, object args)
    {
        Remove(sender, cancel: false);
        if (!_starting && _pending.Count == 0) _completion.TrySetResult(true);
    }

    private void Remove(ConnectedAnimation animation, bool cancel)
    {
        animation.Completed -= OnCompleted;
        _pending.Remove(animation);
        if (cancel) animation.Cancel();
    }
}
