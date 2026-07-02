using CommunityToolkit.Mvvm.ComponentModel;
using HyPlayer.Domain;
using HyPlayer.Domain.Music;
using HyPlayer.Domain.Settings;
using HyPlayer.PlayCore.Abstraction.Interfaces.ProvidableItem;
using HyPlayer.PlayCore.Abstraction.Models;
using HyPlayer.PlayCore.Abstraction.Models.Containers;
using HyPlayer.PlayCore.Abstraction.Models.SingleItems;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Windows.UI.Xaml;

namespace HyPlayer.UI.Lists;

[ObservableObject]
public sealed partial class ProvidableItemRowViewModel
{
    private const string DefaultCoverUrl = "http://p4.music.126.net/UeTuwE7pvjBpypWLudqukA==/3132508627578625.jpg";

    public required ProvidableItemBase Item { get; init; }
    public required int Order { get; init; }
    public required string Title { get; init; }
    public required string LineOne { get; init; }
    public required string LineTwo { get; init; }
    public required string LineThree { get; init; }
    public required string? CoverUrl { get; init; }
    public required string? RichMediaId { get; init; }
    public required bool CanOpenComments { get; init; }
    public required bool CanOpenRichMedia { get; init; }
    public required bool CanOpenCreators { get; init; }
    public required bool CanDownload { get; init; }
    public required bool CanCollect { get; init; }
    public required bool IsAvailable { get; init; }
    public required IReadOnlyList<PersonBase> Creators { get; init; }
    public required AlbumBase? Album { get; init; }
    public required string GroupKey { get; init; }

    [ObservableProperty]
    public partial bool IsCurrent { get; set; }

    partial void OnIsCurrentChanged(bool value)
    {
        OnPropertyChanged(nameof(CurrentVisible));
    }

    public int DisplayOrder => Order + 1;
    public string ItemId => Item.ItemId;
    public string ActualId => Item.ActualId ?? string.Empty;
    public string TypeId => Item.TypeId;
    public string ProviderId => Item.ProviderId;
    public bool HasCover => !string.IsNullOrWhiteSpace(CoverUrl);
    public bool HasLineOne => !string.IsNullOrWhiteSpace(LineOne);
    public bool HasLineTwo => !string.IsNullOrWhiteSpace(LineTwo);
    public bool HasLineThree => !string.IsNullOrWhiteSpace(LineThree);
    public SingleSongBase? AsPlayableSong => Item as SingleSongBase;
    public Visibility CurrentVisible => IsCurrent ? Visibility.Visible : Visibility.Collapsed;
    public Visibility LineOneVisible => HasLineOne ? Visibility.Visible : Visibility.Collapsed;
    public Visibility LineTwoVisible => HasLineTwo ? Visibility.Visible : Visibility.Collapsed;
    public Visibility LineThreeVisible => HasLineThree ? Visibility.Visible : Visibility.Collapsed;

    public Uri? Cover => new Uri((string.IsNullOrEmpty(CoverUrl) ? DefaultCoverUrl : CoverUrl) + "?param=" + StaticSource.PICSIZE_SINGLENCSONG_COVER);

    public bool MatchesFilter(string filterText)
    {
        if (string.IsNullOrWhiteSpace(filterText))
            return true;

        return Contains(Title, filterText)
               || Contains(LineOne, filterText)
               || Contains(LineTwo, filterText)
               || Contains(LineThree, filterText)
               || Contains(ActualId, filterText);
    }

    private static bool Contains(string? text, string filterText)
    {
        return (text ?? string.Empty).Contains(filterText, StringComparison.OrdinalIgnoreCase);
    }
}

public sealed partial class ProvidableItemRowGroup(IEnumerable<ProvidableItemRowViewModel> items) : ObservableCollection<ProvidableItemRowViewModel>(items)
{
    public string Key { get; set; } = string.Empty;
}
