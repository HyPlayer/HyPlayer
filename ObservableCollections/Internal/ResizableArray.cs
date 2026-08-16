using System;
using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace ObservableCollections.Internal
{
    // internal ref struct ResizableArray<T>
    internal struct ResizableArray<T> : IDisposable
    {
        T[]? array;
        int count;

        public ReadOnlySpan<T> Span => array.AsSpan(0, count);

        public ResizableArray(int initialCapacity)
        {
            array = ArrayPool<T>.Shared.Rent(initialCapacity);
            count = 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Add(T item)
        {
            if (array == null) Throw();
            if (array.Length == count)
            {
                EnsureCapacity();
            }
            array[count++] = item;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        void EnsureCapacity()
        {
            var oldArray = array!;
            var newArray = ArrayPool<T>.Shared.Rent(oldArray.Length * 2);
            Array.Copy(oldArray, newArray, oldArray.Length);
            ArrayPool<T>.Shared.Return(oldArray, RuntimeHelpersEx.IsReferenceOrContainsReferences<T>());
            array = newArray;
        }

        public void Dispose()
        {
            if (array != null)
            {
                ArrayPool<T>.Shared.Return(array, RuntimeHelpersEx.IsReferenceOrContainsReferences<T>());
                array = null;
            }
        }

        [DoesNotReturn]
        void Throw()
        {
            throw new ObjectDisposedException("ResizableArray");
        }
    }
}
