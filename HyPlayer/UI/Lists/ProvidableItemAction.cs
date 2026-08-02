using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace HyPlayer.UI.Lists;

public sealed class ProvidableItemAction
{
    public required string Text { get; init; }
    public required Func<ProvidableItemRowViewModel, Task> ExecuteAsync { get; init; }
    public Func<ProvidableItemRowViewModel, bool>? CanExecute { get; init; }
}

public sealed class ProvidableSelectionAction
{
    public required string Text { get; init; }
    public required Func<IReadOnlyList<ProvidableItemRowViewModel>, Task> ExecuteAsync { get; init; }
    public Func<IReadOnlyList<ProvidableItemRowViewModel>, bool>? CanExecute { get; init; }
}