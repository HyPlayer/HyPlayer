using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace HyPlayer.UI.Lists.IncrementalLoading;

public readonly record struct IncrementalPage<T>(IReadOnlyList<T> Items, bool HasMore);

public interface IIncrementalPageSource<T>
{
    bool HasMore { get; }

    Task<IncrementalPage<T>> LoadNextAsync(int desiredCount, CancellationToken cancellationToken);
}
