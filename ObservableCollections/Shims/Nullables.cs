using System;
using System.Collections.Generic;
using System.Text;

#if NETSTANDARD2_0 || NET_STANDARD_2_0 || NET_4_6

namespace System.Diagnostics.CodeAnalysis
{
    internal sealed class MaybeNullWhenAttribute : Attribute
    {
        public MaybeNullWhenAttribute(bool returnValue)
        {
        }
    }

    internal sealed class DoesNotReturnAttribute : Attribute
    {
        public DoesNotReturnAttribute()
        {
        }
    }
}

#endif