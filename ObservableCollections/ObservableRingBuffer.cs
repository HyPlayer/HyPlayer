using ObservableCollections.Internal;
using System;
using System.Linq;
using System.Collections.Generic;
using System.Collections;

namespace ObservableCollections
{
    public partial class ObservableRingBuffer<T> : IList<T>, IReadOnlyList<T>, IObservableCollection<T>
    {
        readonly RingBuffer<T> buffer;

        public event NotifyCollectionChangedEventHandler<T>? CollectionChanged;

        public ObservableRingBuffer()
        {
            this.buffer = new RingBuffer<T>();
        }

        public ObservableRingBuffer(IEnumerable<T> collection)
        {
            this.buffer = new RingBuffer<T>(collection);
        }

        public bool IsReadOnly => false;

        public object SyncRoot { get; } = new object();

        public T this[int index]
        {
            get
            {
                lock (SyncRoot)
                {
                    return this.buffer[index];
                }
            }
            set
            {
                lock (SyncRoot)
                {
                    var oldValue = buffer[index];
                    buffer[index] = value;
                    CollectionChanged?.Invoke(NotifyCollectionChangedEventArgs<T>.Replace(value, oldValue, index, index));
                }
            }
        }

        public int Count
        {
            get
            {
                lock (SyncRoot)
                {
                    return buffer.Count;
                }
            }
        }

        public void AddFirst(T item)
        {
            lock (SyncRoot)
            {
                buffer.AddFirst(item);
                CollectionChanged?.Invoke(NotifyCollectionChangedEventArgs<T>.Add(item, 0));
            }
        }

        public void AddLast(T item)
        {
            lock (SyncRoot)
            {
                buffer.AddLast(item);
                CollectionChanged?.Invoke(NotifyCollectionChangedEventArgs<T>.Add(item, buffer.Count - 1));
            }
        }

        public T RemoveFirst()
        {
            lock (SyncRoot)
            {
                var item = buffer.RemoveFirst();
                CollectionChanged?.Invoke(NotifyCollectionChangedEventArgs<T>.Remove(item, 0));
                return item;
            }
        }

        public T RemoveLast()
        {
            lock (SyncRoot)
            {
                var index = buffer.Count - 1;
                var item = buffer.RemoveLast();
                CollectionChanged?.Invoke(NotifyCollectionChangedEventArgs<T>.Remove(item, index));
                return item;
            }
        }

        // AddFirstRange is not exists.

        public void AddLastRange(IEnumerable<T> items)
        {
            lock (SyncRoot)
            {
                var index = buffer.Count;
                using (var xs = new CloneCollection<T>(items))
                {
                    foreach (var item in xs.Span)
                    {
                        buffer.AddLast(item);
                    }
                    CollectionChanged?.Invoke(NotifyCollectionChangedEventArgs<T>.Add(xs.Span, index));
                }
            }
        }

        public void AddLastRange(T[] items)
        {
            lock (SyncRoot)
            {
                var index = buffer.Count;
                foreach (var item in items)
                {
                    buffer.AddLast(item);
                }
                CollectionChanged?.Invoke(NotifyCollectionChangedEventArgs<T>.Add(items, index));
            }
        }

        public void AddLastRange(ReadOnlySpan<T> items)
        {
            lock (SyncRoot)
            {
                var index = buffer.Count;
                foreach (var item in items)
                {
                    buffer.AddLast(item);
                }
                CollectionChanged?.Invoke(NotifyCollectionChangedEventArgs<T>.Add(items, index));
            }
        }

        public int IndexOf(T item)
        {
            lock (SyncRoot)
            {
                return buffer.IndexOf(item);
            }
        }

        void IList<T>.Insert(int index, T item)
        {
            throw new NotSupportedException();
        }

        bool ICollection<T>.Remove(T item)
        {
            throw new NotSupportedException();
        }

        void IList<T>.RemoveAt(int index)
        {
            throw new NotSupportedException();
        }

        void ICollection<T>.Add(T item)
        {
            AddLast(item);
        }

        public void Clear()
        {
            lock (SyncRoot)
            {
                buffer.Clear();
                CollectionChanged?.Invoke(NotifyCollectionChangedEventArgs<T>.Reset());
            }
        }

        public bool Contains(T item)
        {
            lock (SyncRoot)
            {
                return buffer.Contains(item);
            }
        }

        public void CopyTo(T[] array, int arrayIndex)
        {
            lock (SyncRoot)
            {
                buffer.CopyTo(array, arrayIndex);
            }
        }

        public T[] ToArray()
        {
            lock (SyncRoot)
            {
                return buffer.ToArray();
            }
        }

        public int BinarySearch(T item)
        {
            lock (SyncRoot)
            {
                return buffer.BinarySearch(item);
            }
        }

        public int BinarySearch(T item, IComparer<T> comparer)
        {
            lock (SyncRoot)
            {
                return buffer.BinarySearch(item, comparer);
            }
        }

        public IEnumerator<T> GetEnumerator()
        {
            lock (SyncRoot)
            {
                foreach (var item in buffer)
                {
                    yield return item;
                }
            }
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }
}
