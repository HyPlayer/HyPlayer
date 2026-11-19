#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace HyPlayer.Platform.Storage;

/// <summary>Stores bounded access results and merges probes without blocking readers.</summary>
internal sealed class AsyncFolderAccessCache(Func<string, CancellationToken, Task<bool>> probe,
    TimeSpan timeout, int capacity = 32)
{
    private sealed class Entry
    {
        public readonly TaskCompletionSource<bool> Completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool? Result;
    }

    private readonly object _gate = new();
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly Queue<(string Path, Entry Entry)> _order = new();

    public bool IsUsable(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        lock (_gate) return _entries.TryGetValue(path, out var entry) && entry.Result == true;
    }

    public Task<bool> ProbeAsync(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return Task.FromResult(false);
        Entry entry;
        lock (_gate)
        {
            if (_entries.TryGetValue(path, out var existing)) return existing.Completion.Task;
            entry = new Entry();
            _entries.Add(path, entry);
            _order.Enqueue((path, entry));
            Trim();
        }
        // Completion catches probe failures; its lifetime is bounded by timeout.
        _ = CompleteAsync(path, entry);
        return entry.Completion.Task;
    }

    public void Forget(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        lock (_gate)
        {
            _entries.Remove(path);
            // Remove invalidated queue entries as well, so repeated path changes cannot grow it.
            var count = _order.Count;
            for (var i = 0; i < count; i++)
            {
                var item = _order.Dequeue();
                if (_entries.TryGetValue(item.Path, out var current) && ReferenceEquals(current, item.Entry))
                    _order.Enqueue(item);
            }
        }
    }

    public bool MarkUnusable(string path)
    {
        lock (_gate)
        {
            var wasUsable = _entries.TryGetValue(path, out var existing) && existing.Result == true;
            var entry = new Entry { Result = false };
            entry.Completion.SetResult(false);
            _entries[path] = entry;
            _order.Enqueue((path, entry));
            Trim();
            return wasUsable;
        }
    }

    private void Trim()
    {
        while (_order.Count > capacity)
        {
            var item = _order.Dequeue();
            if (_entries.TryGetValue(item.Path, out var current) && ReferenceEquals(current, item.Entry))
                _entries.Remove(item.Path);
        }
    }

    private async Task CompleteAsync(string path, Entry entry)
    {
        bool usable;
        using var cancellation = new CancellationTokenSource(timeout);
        Task<bool>? operation = null;
        try
        {
            operation = probe(path, cancellation.Token);
            usable = await operation.WaitAsync(cancellation.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Some providers cannot cancel their OS operation immediately. Observe its eventual
            // completion without delaying the fallback or allowing it to update the cached result.
            if (operation is not null) _ = ObserveCompletionAsync(operation);
            usable = false;
        }
        catch (Exception)
        {
            // Permission, missing directory, and timed-out probes all select the default directory.
            usable = false;
        }
        lock (_gate)
        {
            if (_entries.TryGetValue(path, out var current) && ReferenceEquals(current, entry))
                entry.Result = usable;
            else
                usable = false; // A changed path or access failure supersedes the old probe.
        }
        entry.Completion.TrySetResult(usable);
    }

    private static async Task ObserveCompletionAsync(Task<bool> operation)
    {
        try { await operation.ConfigureAwait(false); }
        catch (Exception) { /* The original request already returned the timed-out fallback. */ }
    }
}
