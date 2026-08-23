using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.UI.Xaml.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using HyPlayer.Platform.Storage.Cache;
using HyPlayer.PlayCore.Abstraction.Interfaces.PlayListContainer;
using HyPlayer.PlayCore.Abstraction.Interfaces.Provider;
using HyPlayer.PlayCore.Abstraction.Models;
using HyPlayer.PlayCore.Abstraction.Models.Containers;
using HyPlayer.UI.Lists;

namespace HyPlayer.Features.Library;

public partial class FavoriteViewModel(
    IUserLibraryProvidable userLibraryProvider,
    IProviderKnownTypeIds knownTypeIds) : ObservableObject
{
    private string? _currentTag;

    [ObservableProperty] public partial ContainerBase? ContentContainer { get; set; }

    public void OnSelectionChanged(NavigationViewItem item)
    {
        if (item.Tag is not string tag || string.IsNullOrWhiteSpace(tag)
                                           || string.Equals(_currentTag, tag, StringComparison.Ordinal))
            return;

        _currentTag = tag;
        var (kind, pageSize, cachePrefix) = tag switch
        {
            "Album" => (knownTypeIds.AlbumTypeId, 25, "album_sublist"),
            "Artist" => (knownTypeIds.ArtistTypeId, 25, "artist_sublist"),
            "Radio" => (knownTypeIds.RadioChannelTypeId ?? string.Empty, 200, "djchannel_subscribed"),
            _ => (string.Empty, 25, "favorite")
        };

        ContentContainer = string.IsNullOrWhiteSpace(kind)
            ? null
            : new DelegateProgressiveContainer(
                (offset, count, cancellationToken) =>
                    LoadUserLibraryPageAsync(kind, cachePrefix, offset, count, cancellationToken),
                "收藏",
                $"favorite:{tag}",
                kind,
                pageSize: pageSize);
    }

    private async Task<(bool HasMore, List<ProvidableItemBase> Items)> LoadUserLibraryPageAsync(
        string kind,
        string cachePrefix,
        int offset,
        int count,
        CancellationToken cancellationToken)
    {
        var page = await SimpleCacher.GetOrCreateCacheAsync(
            CacheType.Login,
            $"{cachePrefix}_{offset}_{count}",
            async () =>
            {
                if (await userLibraryProvider.GetCurrentUserLibraryContainerAsync(kind)
                    is not IProgressiveLoadingContainer container)
                    return new UserLibraryPage();

                var (hasMore, items) = await container.GetProgressiveItemsListAsync(
                    offset, count, cancellationToken);
                return new UserLibraryPage
                {
                    HasMore = hasMore,
                    Items = items ?? []
                };
            });

        cancellationToken.ThrowIfCancellationRequested();
        return (page?.HasMore ?? false, page?.Items.ToList() ?? []);
    }

    private sealed class UserLibraryPage
    {
        public bool HasMore { get; init; }
        public List<ProvidableItemBase> Items { get; init; } = [];
    }
}
