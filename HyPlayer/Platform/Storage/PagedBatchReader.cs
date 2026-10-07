using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace HyPlayer.Platform.Storage;

internal static class PagedBatchReader
{
    internal static async IAsyncEnumerable<IReadOnlyList<T>> ReadAsync<T>(
        Func<uint, uint, CancellationToken, Task<IReadOnlyList<T>>> readPage,
        uint pageSize,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (pageSize == 0) throw new ArgumentOutOfRangeException(nameof(pageSize));
        uint offset = 0;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var page = await readPage(offset, pageSize, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (page.Count == 0) yield break;
            yield return page;
            offset = checked(offset + (uint)page.Count);
        }
    }
}
