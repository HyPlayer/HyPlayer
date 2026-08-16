using System;
using System.Buffers;
using System.Runtime.CompilerServices;

namespace ObservableCollections.Internal
{
    internal ref struct FixedArray<T>
    {
        public readonly Span<T> Span;
        T[]? array;

        public FixedArray(int size)
        {
            array = ArrayPool<T>.Shared.Rent(size);
            Span = array.AsSpan(0, size);
        }

        public void Dispose()
        {
            if (array != null)
            {
                ArrayPool<T>.Shared.Return(array, RuntimeHelpersEx.IsReferenceOrContainsReferences<T>());
            }
        }
    }

    internal ref struct FixedBoolArray
    {
        public const int StackallocSize = 128;

        public readonly Span<bool> Span;
        bool[]? array;

        public FixedBoolArray(Span<bool> scratchBuffer, int capacity)
        {
            if (scratchBuffer.Length == 0)
            {
                array = ArrayPool<bool>.Shared.Rent(capacity);
                Span = array.AsSpan(0, capacity);
            }
            else
            {
                Span = scratchBuffer;
            }
        }

        public void Dispose()
        {
            if (array != null)
            {
                ArrayPool<bool>.Shared.Return(array);
            }
        }
    }
}
