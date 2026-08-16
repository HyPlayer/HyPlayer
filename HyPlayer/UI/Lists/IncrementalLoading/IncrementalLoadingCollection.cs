using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.UI.Xaml.Data;

namespace HyPlayer.UI.Lists.IncrementalLoading;

public sealed class IncrementalLoadingCollection<T>(
    IncrementalLoadController<T> controller,
    Func<T, string?>? keySelector = null) : ObservableCollection<T>, ISupportIncrementalLoading
{
    private readonly HashSet<string> _loadedKeys = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _loadGate = new(1, 1);
    private bool _isLoading;

    public event EventHandler? LoadCompleted;
    public event EventHandler<Exception>? LoadFailed;

    public IncrementalLoadController<T> Controller { get; } = controller;
    public bool HasMoreItems => !_isLoading && Controller.CanAutoLoad;

    public void Reset(IIncrementalPageSource<T>? source)
    {
        Controller.Reset(source);
        _loadedKeys.Clear();
        Clear();
    }

    public Task<LoadMoreItemsResult> LoadInitialAsync(int desiredCount, CancellationToken cancellationToken = default)
    {
        return LoadMoreCoreAsync((uint)Math.Max(1, desiredCount), cancellationToken);
    }

    public Task<LoadMoreItemsResult> RetryAsync(int desiredCount, CancellationToken cancellationToken = default)
    {
        Controller.PrepareRetry();
        return LoadMoreCoreAsync((uint)Math.Max(1, desiredCount), cancellationToken);
    }

    public IAsyncOperation<LoadMoreItemsResult> LoadMoreItemsAsync(uint count)
    {
        return AsyncInfo.Run(cancellationToken => LoadMoreCoreAsync(count, cancellationToken));
    }

    private async Task<LoadMoreItemsResult> LoadMoreCoreAsync(uint count, CancellationToken cancellationToken)
    {
        var enteredGate = false;
        var addedCount = 0u;
        try
        {
            await _loadGate.WaitAsync(cancellationToken);
            enteredGate = true;
            _isLoading = true;
            var items = await Controller.LoadNextAsync((int)Math.Clamp(count, 1, int.MaxValue), cancellationToken);
            foreach (var item in items)
            {
                var key = keySelector?.Invoke(item);
                if (key is not null && !_loadedKeys.Add(key))
                    continue;

                Add(item);
                addedCount++;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            LoadFailed?.Invoke(this, ex);
        }
        finally
        {
            if (enteredGate)
            {
                _isLoading = false;
                _loadGate.Release();
                LoadCompleted?.Invoke(this, EventArgs.Empty);
            }
        }

        return new LoadMoreItemsResult { Count = addedCount };
    }
}
