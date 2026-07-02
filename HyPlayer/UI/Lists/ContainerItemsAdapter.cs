using HyPlayer.PlayCore.Abstraction.Interfaces.PlayListContainer;
using HyPlayer.PlayCore.Abstraction.Models;
using HyPlayer.PlayCore.Abstraction.Models.Containers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace HyPlayer.UI.Lists;

public sealed class StaticItemsContainer : LinerContainerBase
{
    private readonly IReadOnlyList<ProvidableItemBase> _items;

    public StaticItemsContainer(IEnumerable<ProvidableItemBase> items, string name = "", string actualId = "static", string typeId = "static")
    {
        _items = items.ToList();
        Name = name;
        ActualId = actualId;
        TypeIdValue = typeId;
    }

    public string TypeIdValue { get; }
    public override string ProviderId => string.Empty;
    public override string TypeId => TypeIdValue;

    public override Task<List<ProvidableItemBase>> GetAllItemsAsync(CancellationToken ctk = default)
    {
        return Task.FromResult(_items.ToList());
    }
}


public sealed class ReorderedContainer : LinerContainerBase
{
    private readonly ContainerBase _source;
    private readonly bool _reverse;

    public ReorderedContainer(ContainerBase source, bool reverse)
    {
        _source = source;
        _reverse = reverse;
        Name = source.Name;
        ActualId = source.ActualId;
    }

    public override string ProviderId => _source.ProviderId;
    public override string TypeId => _source.TypeId;

    public override async Task<List<ProvidableItemBase>> GetAllItemsAsync(CancellationToken ctk = default)
    {
        var items = await LoadAllAsync(_source, ctk);
        if (_reverse)
            items.Reverse();
        return items;
    }

    private static async Task<List<ProvidableItemBase>> LoadAllAsync(ContainerBase source, CancellationToken ctk)
    {
        return source switch
        {
            LinerContainerBase liner => await liner.GetAllItemsAsync(ctk),
            IProgressiveLoadingContainer progressive => (await progressive.GetProgressiveItemsListAsync(0, progressive.MaxProgressiveCount, ctk)).Item2,
            UndeterminedContainerBase undetermined => await undetermined.GetNextItemsRangeAsync(ctk),
            _ => []
        };
    }
}
