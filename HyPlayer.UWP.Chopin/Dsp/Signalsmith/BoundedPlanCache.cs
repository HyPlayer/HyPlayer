using System;
using System.Collections.Generic;
using System.Threading;

namespace HyPlayer.UWP.Chopin.Dsp.Signalsmith;

/// <summary>Constructor-only LRU of immutable numeric tables, bounded by count and bytes.</summary>
internal sealed class BoundedPlanCache<TKey, TPlan>(Func<TKey, TPlan> create, Func<TPlan, long> size, long byteLimit)
    where TKey : notnull
{
    private sealed class Entry(TPlan plan, long used, long bytes)
    {
        public readonly TPlan Plan = plan;
        public readonly long Bytes = bytes;
        public long Used = used;
    }
    private readonly Lock _gate = new();
    private readonly Dictionary<TKey, Entry> _entries = [];
    private long _clock, _bytes;

    public TPlan Get(TKey key)
    {
        lock (_gate)
        {
            if (_entries.TryGetValue(key, out var cached))
            {
                cached.Used = ++_clock;
                return cached.Plan;
            }
            var plan = create(key);
            var bytes = size(plan);
            if (bytes > byteLimit) return plan; // Large custom configurations are not retained globally.
            while (_entries.Count >= 8 || _bytes + bytes > byteLimit)
            {
                var oldest = default(TKey);
                var age = long.MaxValue;
                foreach (var entry in _entries)
                    if (entry.Value.Used < age) { oldest = entry.Key; age = entry.Value.Used; }
                _bytes -= _entries[oldest].Bytes;
                _entries.Remove(oldest);
            }
            _entries.Add(key, new Entry(plan, ++_clock, bytes));
            _bytes += bytes;
            return plan;
        }
    }
}
