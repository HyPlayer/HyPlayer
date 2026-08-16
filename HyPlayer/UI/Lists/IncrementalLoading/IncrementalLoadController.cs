using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace HyPlayer.UI.Lists.IncrementalLoading;

public sealed partial class IncrementalLoadController<T> : INotifyPropertyChanged, IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private CancellationTokenSource _sourceCancellation = new();
    private IIncrementalPageSource<T>? _source;
    private long _generation;
    private Exception? _lastError;
    private int _loadedCount;
    private IncrementalLoadStatus _status = IncrementalLoadStatus.Exhausted;

    public event PropertyChangedEventHandler? PropertyChanged;

    public IncrementalLoadStatus Status
    {
        get => _status;
        private set
        {
            if (_status == value)
                return;

            _status = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsLoading));
            OnPropertyChanged(nameof(CanAutoLoad));
            OnPropertyChanged(nameof(CanRetry));
        }
    }

    public Exception? LastError
    {
        get => _lastError;
        private set
        {
            if (ReferenceEquals(_lastError, value))
                return;

            _lastError = value;
            OnPropertyChanged();
        }
    }

    public bool HasMore => _source?.HasMore is true;
    public bool IsLoading => Status is IncrementalLoadStatus.InitialLoading or IncrementalLoadStatus.LoadingMore;
    public bool CanAutoLoad => HasMore && Status is IncrementalLoadStatus.Idle or IncrementalLoadStatus.Canceled;
    public bool CanRetry => HasMore && Status is IncrementalLoadStatus.Failed;

    public void Reset(IIncrementalPageSource<T>? source)
    {
        CancelPendingCore();
        _source = source;
        _loadedCount = 0;
        LastError = null;
        Status = source?.HasMore is true ? IncrementalLoadStatus.Idle : IncrementalLoadStatus.Exhausted;
        NotifyAvailabilityChanged();
    }

    public void CancelPending()
    {
        CancelPendingCore();
        if (IsLoading)
            Status = IncrementalLoadStatus.Canceled;
    }

    public void PrepareRetry()
    {
        if (Status is not IncrementalLoadStatus.Failed)
            return;

        LastError = null;
        Status = HasMore ? IncrementalLoadStatus.Idle : IncrementalLoadStatus.Exhausted;
    }

    public async Task<IReadOnlyList<T>> LoadNextAsync(int desiredCount, CancellationToken cancellationToken)
    {
        var generation = _generation;
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, _sourceCancellation.Token);
        var token = linkedCancellation.Token;
        var enteredGate = false;

        try
        {
            await _gate.WaitAsync(token);
            enteredGate = true;
            if (generation != _generation || _source is null || !_source.HasMore)
            {
                if (generation == _generation)
                    Status = IncrementalLoadStatus.Exhausted;
                return [];
            }

            Status = _loadedCount == 0
                ? IncrementalLoadStatus.InitialLoading
                : IncrementalLoadStatus.LoadingMore;
            LastError = null;

            var page = await _source.LoadNextAsync(Math.Max(1, desiredCount), token);
            token.ThrowIfCancellationRequested();
            if (generation != _generation)
                return [];

            _loadedCount += page.Items.Count;
            Status = page.HasMore ? IncrementalLoadStatus.Idle : IncrementalLoadStatus.Exhausted;
            NotifyAvailabilityChanged();
            return page.Items;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            if (generation == _generation)
                Status = IncrementalLoadStatus.Canceled;
            return [];
        }
        catch (Exception ex)
        {
            if (generation == _generation)
            {
                LastError = ex;
                Status = IncrementalLoadStatus.Failed;
            }

            throw;
        }
        finally
        {
            if (enteredGate)
                _gate.Release();
        }
    }

    public void Dispose()
    {
        _sourceCancellation.Cancel();
        _sourceCancellation.Dispose();
        _gate.Dispose();
    }

    private void CancelPendingCore()
    {
        _generation++;
        _sourceCancellation.Cancel();
        _sourceCancellation.Dispose();
        _sourceCancellation = new CancellationTokenSource();
    }

    private void NotifyAvailabilityChanged()
    {
        OnPropertyChanged(nameof(HasMore));
        OnPropertyChanged(nameof(CanAutoLoad));
        OnPropertyChanged(nameof(CanRetry));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
