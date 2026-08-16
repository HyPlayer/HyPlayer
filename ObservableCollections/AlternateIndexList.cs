#pragma warning disable CS0436

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Runtime.InteropServices;

namespace ObservableCollections;

public partial class AlternateIndexList<T> : IEnumerable<T>
{
    List<IndexedValue> list; // alternate index is ordered

    public AlternateIndexList()
    {
        this.list = new();
    }

    public AlternateIndexList(IEnumerable<(int OrderedAlternateIndex, T Value)> values)
    {
        this.list = values.Select(x => new IndexedValue(x.OrderedAlternateIndex, x.Value)).ToList();
    }

    public void UpdateAlternateIndex(int startIndex, int incr)
    {
        var span = CollectionsMarshal.AsSpan(list);
        for (int i = startIndex; i < span.Length; i++)
        {
            span[i].AlternateIndex += incr;
        }
    }

    public T this[int index]
    {
        get => list[index].Value;
        set => CollectionsMarshal.AsSpan(list)[index].Value = value;
    }

    public int GetAlternateIndex(int index) => list[index].AlternateIndex;

    public int Count => list.Count;

    public int Insert(int alternateIndex, T value)
    {
        var index = list.BinarySearch(alternateIndex);
        if (index < 0)
        {
            index = ~index;
        }
        list.Insert(index, new(alternateIndex, value));
        UpdateAlternateIndex(index + 1, 1);
        return index;
    }

    public int InsertRange(int startingAlternateIndex, IEnumerable<T> values)
    {
        var index = list.BinarySearch(startingAlternateIndex);
        if (index < 0)
        {
            index = ~index;
        }

        using var iter = new InsertIterator(startingAlternateIndex, values);
        list.InsertRange(index, iter);
        UpdateAlternateIndex(index + iter.ConsumedCount, iter.ConsumedCount);
        return index;
    }

    public int Remove(T value)
    {
        var index = list.FindIndex(x => EqualityComparer<T>.Default.Equals(x.Value, value));
        if (index != -1)
        {
            list.RemoveAt(index);
            UpdateAlternateIndex(index, -1);
        }
        return index;
    }

    public int RemoveAt(int alternateIndex)
    {
        var index = list.BinarySearch(alternateIndex);
        if (index >= 0)
        {
            list.RemoveAt(index);
            UpdateAlternateIndex(index, -1);
        }
        else
        {
            throw new InvalidOperationException("Index was not found. AlternateIndex:" + alternateIndex);
        }
        return index;
    }

    public int RemoveRange(int alternateIndex, int count)
    {
        var index = list.BinarySearch(alternateIndex);
        if (index < 0)
        {
            index = ~index;
        }

        list.RemoveRange(index, count);
        UpdateAlternateIndex(index, -count);
        return index;
    }

    public bool TryGetAtAlternateIndex(int alternateIndex, [MaybeNullWhen(true)] out T value)
    {
        var index = list.BinarySearch(alternateIndex);
        if (index < 0)
        {
            value = default!;
            return false;
        }
        value = list[index].Value!;
        return true;
    }

    public bool TrySetAtAlternateIndex(int alternateIndex, T value, out int setIndex)
    {
        setIndex = list.BinarySearch(alternateIndex);
        if (setIndex < 0)
        {
            return false;
        }
        CollectionsMarshal.AsSpan(list)[setIndex].Value = value;
        return true;
    }

    /// <summary>NOTE: when replace successfully, list has been sorted.</summary>
    public bool TryReplaceAlternateIndex(int getAlternateIndex, int setAlternateIndex)
    {
        var index = list.BinarySearch(getAlternateIndex);
        if (index < 0)
        {
            return false;
        }

        var span = CollectionsMarshal.AsSpan(list);
        span[index].AlternateIndex = setAlternateIndex;
        list.Sort(); // needs sort to keep order
        return true;
    }

    public bool TryReplaceByValue(T searchValue, T replaceValue, out int replacedIndex)
    {
        replacedIndex = list.FindIndex(x => EqualityComparer<T>.Default.Equals(x.Value, searchValue));
        if (replacedIndex != -1)
        {
            CollectionsMarshal.AsSpan(list)[replacedIndex].Value = replaceValue;
            return true;
        }
        return false;
    }

    public void Clear()
    {
        list.Clear();
    }

    public void Clear(IEnumerable<(int OrderedAlternateIndex, T Value)> values)
    {
        list.Clear();
        list.AddRange(values.Select(x => new IndexedValue(x.OrderedAlternateIndex, x.Value)));
    }

    public IEnumerator<T> GetEnumerator()
    {
        foreach (var item in list)
        {
            yield return item.Value;
        }
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    public IEnumerable<(int AlternateIndex, T Value)> GetIndexedValues()
    {
        foreach (var item in list)
        {
            yield return (item.AlternateIndex, item.Value);
        }
    }

    class InsertIterator(int startingIndex, IEnumerable<T> values) : IEnumerable<IndexedValue>, IEnumerator<IndexedValue>
    {
        IEnumerator<T> iter = values.GetEnumerator();
        IndexedValue current;

        public int ConsumedCount { get; private set; }

        public IndexedValue Current => current;

        object IEnumerator.Current => Current;

        public void Dispose() => iter.Dispose();

        public bool MoveNext()
        {
            if (iter.MoveNext())
            {
                ConsumedCount++;
                current = new(startingIndex++, iter.Current);
                return true;
            }
            return false;
        }

        public void Reset() { }

        public IEnumerator<IndexedValue> GetEnumerator() => this;

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    struct IndexedValue : IComparable<IndexedValue>
    {
        public int AlternateIndex; // mutable
        public T Value; // mutable

        public IndexedValue(int alternateIndex, T value)
        {
            this.AlternateIndex = alternateIndex;
            this.Value = value;
        }

        public static implicit operator IndexedValue(int alternateIndex) // for query
        {
            return new IndexedValue(alternateIndex, default!);
        }

        public int CompareTo(IndexedValue other)
        {
            return AlternateIndex.CompareTo(other.AlternateIndex);
        }

        public override string ToString()
        {
            return (AlternateIndex, Value).ToString();
        }
    }
}
