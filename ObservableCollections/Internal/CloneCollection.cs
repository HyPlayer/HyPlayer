using System;
using System.Buffers;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Linq;

namespace ObservableCollections.Internal
{
    /// <summary>
    /// ReadOnly cloned collection.
    /// </summary>
    internal struct CloneCollection<T> : IDisposable
    {
        T[]? array;
        int length;

        public ReadOnlySpan<T> Span => array.AsSpan(0, length);

        public IEnumerable<T> AsEnumerable() => new EnumerableCollection(array, length);

        public CloneCollection(T item)
        {
            this.array = ArrayPool<T>.Shared.Rent(1);
            this.length = 1;
            this.array[0] = item;
        }

        public CloneCollection(IEnumerable<T> source)
        {
            if (source.TryGetNonEnumeratedCount(out var count))
            {
                var array = ArrayPool<T>.Shared.Rent(count);

                if (source is ICollection<T> c)
                {
                    c.CopyTo(array, 0);
                }
                else
                {
                    var i = 0;
                    foreach (var item in source)
                    {
                        array[i++] = item;
                    }
                }
                this.array = array;
                this.length = count;
            }
            else
            {
                var array = ArrayPool<T>.Shared.Rent(16);

                var i = 0;
                foreach (var item in source)
                {
                    TryEnsureCapacity(ref array, i);
                    array[i++] = item;
                }
                this.array = array;
                this.length = i;
            }
        }

        public CloneCollection(ReadOnlySpan<T> source)
        {
            var array = ArrayPool<T>.Shared.Rent(source.Length);
            source.CopyTo(array);
            this.array = array;
            this.length = source.Length;
        }

        static void TryEnsureCapacity(ref T[] array, int index)
        {
            if (array.Length == index)
            {
                var newArray = ArrayPool<T>.Shared.Rent(index * 2);
                Array.Copy(array, newArray, index);
                ArrayPool<T>.Shared.Return(array, RuntimeHelpersEx.IsReferenceOrContainsReferences<T>());
                array = newArray;
            }
        }

        public void Dispose()
        {
            if (array != null)
            {
                ArrayPool<T>.Shared.Return(array, RuntimeHelpersEx.IsReferenceOrContainsReferences<T>());
                array = null;
            }
        }

        // Optimize to use Count and CopyTo
        class EnumerableCollection : ICollection<T>
        {
            readonly T[] array;
            readonly int count;

            public EnumerableCollection(T[]? array, int count)
            {
                if (array == null)
                {
                    this.array = Array.Empty<T>();
                    this.count = 0;
                }
                else
                {
                    this.array = array;
                    this.count = count;
                }
            }

            public int Count => count;

            public bool IsReadOnly => true;

            public void Add(T item) => throw new NotSupportedException();
            public void Clear() => throw new NotSupportedException();
            public bool Contains(T item) => throw new NotSupportedException();
            public void CopyTo(T[] dest, int destIndex) =>  Array.Copy(array, 0, dest, destIndex, count);

            public IEnumerator<T> GetEnumerator()
            {
                for (int i = 0; i < count; i++)
                {
                    yield return array[i];
                }
            }

            public bool Remove(T item) => throw new NotSupportedException();
            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        }
    }
}