using System.Collections.Specialized;
using HyPlayer.PlayCore.Abstraction.Models;
using HyPlayer.PlayCore.Abstraction.Models.Containers;
using HyPlayer.PlayCore.Abstraction.Models.SingleItems;
using HyPlayer.UI.Lists;
using ObservableCollections;
using TUnit.Core;

namespace HyPlayer.Playback.Tests;

public sealed class ContainerItemsViewStateTests
{
    [Test]
    public void Append_and_unchanged_filter_preserve_rows_and_emit_no_reset()
    {
        var rows = new ObservableList<ProvidableItemRowViewModel>();
        using var state = new ContainerItemsViewState(rows);
        var first = Row("first");
        rows.Add(first);
        var events = new List<NotifyCollectionChangedEventArgs>();
        state.RowsView.CollectionChanged += (_, args) => events.Add(args);

        rows.AddRange(new[] { Row("second"), Row("third") });
        state.ApplyFilter("");
        state.ApplyFilter("  ");

        Check(events.Count == 2 && events.All(e => e.Action == NotifyCollectionChangedAction.Add),
            "Appending and submitting the unchanged empty filter must emit only Add.");
        Check(events.Select(e => e.NewStartingIndex).SequenceEqual(new[] { 1, 2 }), "Append indices changed.");
        Check(ReferenceEquals(state.RowsView[0], first), "The first row was recreated.");

        state.ApplyFilter("first");
        events.Clear();
        rows.AddRange(new[] { Row("other"), Row("first again") });
        state.ApplyFilter("first");
        Check(events.Count == 1 && events[0].Action == NotifyCollectionChangedAction.Add,
            "A retained filter must filter new items without resetting existing rows.");
        Check(state.RowsView.Count == 2 && ReferenceEquals(state.RowsView[0], first), "Filtered rows changed.");
    }

    [Test]
    public void Disc_groups_append_to_stable_notifying_views_and_keep_unknown_discs()
    {
        var rows = new ObservableList<ProvidableItemRowViewModel>();
        using var state = new ContainerItemsViewState(rows);
        state.IsGrouped = true;
        var first = Row("first", "01");
        rows.Add(first);
        var group = state.GroupedItems.Single();
        var view = group.Items;
        var events = new List<NotifyCollectionChangedAction>();
        view.CollectionChanged += (_, args) => events.Add(args.Action);

        rows.Add(Row("second", "01"));
        rows.Add(Row("third", "02"));
        rows.Add(Row("unknown"));

        Check(ReferenceEquals(state.GroupedItems.Single(g => g.Key == "01"), group), "The disc was recreated.");
        Check(ReferenceEquals(group.Items, view) && ReferenceEquals(view[0], first), "The inner view changed.");
        Check(events.SequenceEqual(new[] { NotifyCollectionChangedAction.Add }), "An existing disc was reset.");
        Check(state.GroupedItems.Select(g => g.Key).SequenceEqual(new[] { "", "01", "02" }), "Disc order changed.");
        Check(state.GroupedItems.Single(g => g.Key == "").Items.Count == 1, "Unknown disc songs disappeared.");
        Check(group.ShowHeader, "Multiple discs should display their titles.");

        state.ApplyFilter("first");
        Check(state.GroupedItems.Count == 1 && !state.GroupedItems[0].ShowHeader, "Single visible disc should hide its title.");
        state.ApplyFilter("");
        Check(state.GroupedItems.Sum(g => g.Items.Count) == 4, "Clearing the filter lost rows.");
        rows.Clear();
        Check(state.GroupedItems.Count == 0, "A source reset left old groups behind.");
    }

    private static ProvidableItemRowViewModel Row(string title, string disc = "") => new()
    {
        Item = new Song(title), Order = 0, Title = title, Translation = "", LineOne = "", LineTwo = "", LineThree = "",
        CoverUrl = null, RichMediaId = null, CanOpenComments = false, CanOpenRichMedia = false,
        CanOpenCreators = false, CanDownload = false, CanCollect = false, IsAvailable = true,
        Creators = [], Album = null, GroupKey = disc
    };

    [Test]
    public void Group_items_path_is_exposed_by_compiled_Xaml_metadata()
    {
        using var group = new ProvidableItemRowGroup("01", new[] { Row("first", "01") });
        var provider = new HyPlayer.HyPlayer_XamlTypeInfo.XamlMetaDataProvider();
        var type = provider.GetXamlType(typeof(ProvidableItemRowGroup));
        var items = type.GetMember(nameof(ProvidableItemRowGroup.Items));
        Check(items is not null && ReferenceEquals(items.GetValue(group), group.Items),
            "CollectionViewSource.ItemsPath must resolve through generated metadata in AOT builds.");
    }

    private sealed class Song(string id) : SingleSongBase
    {
        public override string ProviderId => "test";
        public override string TypeId => "song";
        public override Task<List<PersonBase>?> GetCreatorsAsync(CancellationToken ctk = default) => Task.FromResult<List<PersonBase>?>([]);
        public override string ToString() => id;
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
