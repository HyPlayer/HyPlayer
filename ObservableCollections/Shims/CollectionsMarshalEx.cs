using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

#if !NET7_0_OR_GREATER

#pragma warning disable CS0649
#pragma warning disable CS8618
#pragma warning disable CS8619

namespace System.Runtime.InteropServices;

internal static class CollectionsMarshal
{
    /// <summary>
    /// similar as AsSpan but modify size to create fixed-size span.
    /// </summary>
    public static Span<T> AsSpan<T>(List<T>? list)
    {
        if (list is null) return default;

        ref var view = ref Unsafe.As<List<T>, ListView<T>>(ref list!);
        return view._items.AsSpan(0, view._size);
    }

    internal sealed class ListView<T>
    {
        public T[] _items;
        public int _size;
        public int _version;
    }
}

#endif