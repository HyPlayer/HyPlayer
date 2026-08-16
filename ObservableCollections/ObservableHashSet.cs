using ObservableCollections.Internal;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace ObservableCollections
{
    // can not implements ISet<T> because set operation can not get added/removed values.
    public partial class ObservableHashSet<T> : IReadOnlySet<T>, IReadOnlyCollection<T>, IObservableCollection<T>
        where T : notnull
    {
        readonly HashSet<T> set;
        public object SyncRoot { get; } = new object();

        public ObservableHashSet()
        {
            this.set = new HashSet<T>();
        }

        public ObservableHashSet(IEqualityComparer<T>? comparer)
        {
            this.set = new HashSet<T>(comparer: comparer);
        }

#if NETSTANDARD2_1_OR_GREATER || NET5_0_OR_GREATER

        public ObservableHashSet(int capacity)
        {
            this.set = new HashSet<T>(capacity: capacity);
        }

        public ObservableHashSet(int capacity, IEqualityComparer<T>? comparer)
        {
            this.set = new HashSet<T>(capacity: capacity, comparer: comparer);
        }

#endif

        public ObservableHashSet(IEnumerable<T> collection)
        {
            this.set = new HashSet<T>(collection: collection);
        }

        public ObservableHashSet(IEnumerable<T> collection, IEqualityComparer<T>? comparer)
        {
            this.set = new HashSet<T>(collection: collection, comparer: comparer);
        }

        public event NotifyCollectionChangedEventHandler<T>? CollectionChanged;

        public int Count
        {
            get
            {
                lock (SyncRoot)
                {
                    return set.Count;
                }
            }
        }

        public bool IsReadOnly => false;

        public bool Add(T item)
        {
            lock (SyncRoot)
            {
                if (set.Add(item))
                {
                    CollectionChanged?.Invoke(NotifyCollectionChangedEventArgs<T>.Add(item, -1));
                    return true;
                }

                return false;
            }
        }

        public void AddRange(IEnumerable<T> items)
        {
            lock (SyncRoot)
            {
                if (!items.TryGetNonEnumeratedCount(out var capacity))
                {
                    capacity = 4;
                }

                using (var list = new ResizableArray<T>(capacity))
                {
                    foreach (var item in items)
                    {
                        if (set.Add(item))
                        {
                            list.Add(item);
                        }
                    }

                    CollectionChanged?.Invoke(NotifyCollectionChangedEventArgs<T>.Add(list.Span, -1));
                }
            }
        }

        public void AddRange(T[] items)
        {
            AddRange(items.AsSpan());
        }

        public void AddRange(ReadOnlySpan<T> items)
        {
            lock (SyncRoot)
            {
                using (var list = new ResizableArray<T>(items.Length))
                {
                    foreach (var item in items)
                    {
                        if (set.Add(item))
                        {
                            list.Add(item);
                        }
                    }

                    CollectionChanged?.Invoke(NotifyCollectionChangedEventArgs<T>.Add(list.Span, -1));
                }
            }
        }

        public bool Remove(T item)
        {
            lock (SyncRoot)
            {
                if (set.Remove(item))
                {
                    CollectionChanged?.Invoke(NotifyCollectionChangedEventArgs<T>.Remove(item, -1));
                    return true;
                }

                return false;
            }
        }

        public void RemoveRange(IEnumerable<T> items)
        {
            lock (SyncRoot)
            {
                if (!items.TryGetNonEnumeratedCount(out var capacity))
                {
                    capacity = 4;
                }

                using (var list = new ResizableArray<T>(capacity))
                {
                    foreach (var item in items)
                    {
                        if (set.Remove(item))
                        {
                            list.Add(item);
                        }
                    }

                    CollectionChanged?.Invoke(NotifyCollectionChangedEventArgs<T>.Remove(list.Span, -1));
                }
            }
        }

        public void RemoveRange(T[] items)
        {
            RemoveRange(items.AsSpan());
        }

        public void RemoveRange(ReadOnlySpan<T> items)
        {
            lock (SyncRoot)
            {
                using (var list = new ResizableArray<T>(items.Length))
                {
                    foreach (var item in items)
                    {
                        if (set.Remove(item))
                        {
                            list.Add(item);
                        }
                    }

                    CollectionChanged?.Invoke(NotifyCollectionChangedEventArgs<T>.Remove(list.Span, -1));
                }
            }
        }

        public void Clear()
        {
            lock (SyncRoot)
            {
                set.Clear();
                CollectionChanged?.Invoke(NotifyCollectionChangedEventArgs<T>.Reset());
            }
        }

#if !NETSTANDARD2_0 && !NET_STANDARD_2_0 && !NET_4_6

        public bool TryGetValue(T equalValue, [MaybeNullWhen(false)] out T actualValue)
        {
            lock (SyncRoot)
            {
                return set.TryGetValue(equalValue, out actualValue);
            }
        }

#endif

        public bool Contains(T item)
        {
            lock (SyncRoot)
            {
                return set.Contains(item);
            }
        }

        public bool IsProperSubsetOf(IEnumerable<T> other)
        {
            lock (SyncRoot)
            {
                return set.IsProperSubsetOf(other);
            }
        }

        public bool IsProperSupersetOf(IEnumerable<T> other)
        {
            lock (SyncRoot)
            {
                return set.IsProperSupersetOf(other);
            }
        }

        public bool IsSubsetOf(IEnumerable<T> other)
        {
            lock (SyncRoot)
            {
                return set.IsSubsetOf(other);
            }
        }

        public bool IsSupersetOf(IEnumerable<T> other)
        {
            lock (SyncRoot)
            {
                return set.IsSupersetOf(other);
            }
        }

        public bool Overlaps(IEnumerable<T> other)
        {
            lock (SyncRoot)
            {
                return set.Overlaps(other);
            }
        }

        public bool SetEquals(IEnumerable<T> other)
        {
            lock (SyncRoot)
            {
                return set.SetEquals(other);
            }
        }

        public IEnumerator<T> GetEnumerator()
        {
            lock (SyncRoot)
            {
                foreach (var item in set)
                {
                    yield return item;
                }
            }
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        public IEqualityComparer<T> Comparer
        {
            get
            {
                lock (SyncRoot)
                {
                    return set.Comparer;
                }
            }
        }
    }
}
