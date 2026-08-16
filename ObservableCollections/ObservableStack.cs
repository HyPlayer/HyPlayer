using ObservableCollections.Internal;
using System;
using System.Buffers;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace ObservableCollections
{
    public partial class ObservableStack<T> : IReadOnlyCollection<T>, IObservableCollection<T>
    {
        readonly Stack<T> stack;
        public object SyncRoot { get; } = new object();

        public ObservableStack()
        {
            this.stack = new Stack<T>();
        }

        public ObservableStack(int capacity)
        {
            this.stack = new Stack<T>(capacity);
        }

        public ObservableStack(IEnumerable<T> collection)
        {
            this.stack = new Stack<T>(collection);
        }

        public event NotifyCollectionChangedEventHandler<T>? CollectionChanged;

        public int Count
        {
            get
            {
                lock (SyncRoot)
                {
                    return stack.Count;
                }
            }
        }

        public void Push(T item)
        {
            lock (SyncRoot)
            {
                stack.Push(item);
                CollectionChanged?.Invoke(NotifyCollectionChangedEventArgs<T>.Add(item, 0));
            }
        }

        public void PushRange(IEnumerable<T> items)
        {
            lock (SyncRoot)
            {
                using (var xs = new CloneCollection<T>(items))
                {
                    foreach (var item in xs.Span)
                    {
                        stack.Push(item);
                    }
                    CollectionChanged?.Invoke(NotifyCollectionChangedEventArgs<T>.Add(xs.Span, 0));
                }
            }
        }

        public void PushRange(T[] items)
        {
            lock (SyncRoot)
            {
                foreach (var item in items)
                {
                    stack.Push(item);
                }
                CollectionChanged?.Invoke(NotifyCollectionChangedEventArgs<T>.Add(items, 0));
            }
        }

        public void PushRange(ReadOnlySpan<T> items)
        {
            lock (SyncRoot)
            {
                foreach (var item in items)
                {
                    stack.Push(item);
                }
                CollectionChanged?.Invoke(NotifyCollectionChangedEventArgs<T>.Add(items, 0));
            }
        }

        public T Pop()
        {
            lock (SyncRoot)
            {
                var v = stack.Pop();
                CollectionChanged?.Invoke(NotifyCollectionChangedEventArgs<T>.Remove(v, 0));
                return v;
            }
        }

        public bool TryPop([MaybeNullWhen(false)] out T result)
        {
            lock (SyncRoot)
            {
                if (stack.Count != 0)
                {
                    result = stack.Pop();
                    CollectionChanged?.Invoke(NotifyCollectionChangedEventArgs<T>.Remove(result, 0));
                    return true;
                }

                result = default;
                return false;
            }
        }

        public void PopRange(int count)
        {
            lock (SyncRoot)
            {
                var dest = ArrayPool<T>.Shared.Rent(count);
                try
                {
                    for (int i = 0; i < count; i++)
                    {
                        dest[i] = stack.Pop();
                    }

                    CollectionChanged?.Invoke(NotifyCollectionChangedEventArgs<T>.Remove(dest.AsSpan(0, count), 0));
                }
                finally
                {
                    ArrayPool<T>.Shared.Return(dest, RuntimeHelpersEx.IsReferenceOrContainsReferences<T>());
                }
            }
        }

        public void PopRange(Span<T> dest)
        {
            lock (SyncRoot)
            {
                for (int i = 0; i < dest.Length; i++)
                {
                    dest[i] = stack.Pop();
                }

                CollectionChanged?.Invoke(NotifyCollectionChangedEventArgs<T>.Remove(dest, 0));
            }
        }

        public void Clear()
        {
            lock (SyncRoot)
            {
                stack.Clear();
                CollectionChanged?.Invoke(NotifyCollectionChangedEventArgs<T>.Reset());
            }
        }

        public T Peek()
        {
            lock (SyncRoot)
            {
                return stack.Peek();
            }
        }

        public bool TryPeek([MaybeNullWhen(false)] out T result)
        {
            lock (SyncRoot)
            {
                if (stack.Count != 0)
                {
                    result = stack.Peek();
                    return true;
                }
                result = default;
                return false;
            }
        }

        public T[] ToArray()
        {
            lock (SyncRoot)
            {
                return stack.ToArray();
            }
        }

        public void TrimExcess()
        {
            lock (SyncRoot)
            {
                stack.TrimExcess();
            }
        }

        public IEnumerator<T> GetEnumerator()
        {
            lock (SyncRoot)
            {
                foreach (var item in stack)
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
