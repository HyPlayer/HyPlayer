using AsyncAwaitBestPractices;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HyPlayer.Domain.Music;
using HyPlayer.Domain.Navigation;
using HyPlayer.PlayCore.Abstraction.Interfaces.ProvidableItem;
using HyPlayer.PlayCore.Abstraction.Interfaces.PlayListContainer;
using HyPlayer.PlayCore.Abstraction.Interfaces.Provider;
using HyPlayer.PlayCore.Abstraction.Models;
using HyPlayer.PlayCore.Abstraction.Models.Containers;
using HyPlayer.PlayCore.Abstraction.Models.Resources;
using HyPlayer.Services.Abstractions;
using HyPlayer.Services.Cache;
using HyPlayer.UI.Converters;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Windows.UI.Xaml.Controls;

namespace HyPlayer.Features.Library
{
    public partial class FavoriteViewModel : ObservableRecipient
    {
        private readonly IUserLibraryProvidable _userLibraryProvider;
        private readonly IProviderKnownTypeIds _knownTypeIds;
        private readonly INotificationService _notification;

        public FavoriteViewModel(
            IUserLibraryProvidable userLibraryProvider,
            IProviderKnownTypeIds knownTypeIds,
            INotificationService notification)
        {
            _userLibraryProvider = userLibraryProvider;
            _knownTypeIds = knownTypeIds;
            _notification = notification;
        }

        public ObservableCollection<SimpleListItem> Content { get; set; } = new();
        [ObservableProperty]
        public partial int CurrentPage { get; set; }
        [ObservableProperty]
        public partial bool HasMore { get; set; }
        private int _currentIndex = 1;
        private string _currentTag;
        private Task _loadPageTask;
        private readonly HashSet<string> _loadedPages = [];

        public async Task LoadPageContent(string tag, int page)
        {
            switch (tag)
            {
                case "Album":
                    await LoadAlbumResult(page);
                    break;
                case "Artist":
                    await LoadArtistResult(page);
                    break;
                case "Radio":
                    await LoadRadioResult(page);
                    break;
            }
        }

        private async Task LoadRadioResult(int page)
        {
            var jv = await SimpleCacher.GetOrCreateCacheAsync(CacheType.Login, $"djchannel_subscribed_{page}",
                    async () =>
                    {
                        return await LoadUserLibraryPageAsync(_knownTypeIds.RadioChannelTypeId!, page * 200, 200);
                    });


            HasMore = jv.HasMore;
            foreach (var item in jv.Items.OfType<LinerContainerBase>())
            {
                var description = item is IHasDescription descriptionProvider ? descriptionProvider.Description : null;
                var creators = item is IHasCreators creatorsProvider ? await creatorsProvider.GetCreatorsAsync() : null;
                Content.Add(new SimpleListItem
                {
                    Title = item.Name,
                    LineOne = string.Join(" / ", creators?.Select(creator => creator.Name) ?? []),
                    LineTwo = description,
                    LineThree = string.Empty,
                    Route = new AppRoute.Radio($"{item.ActualId}"),
                    PlayResource = new MusicResource.Radio($"{item.ActualId}"),
                    CoverLink = await TryGetCoverLinkAsync(item),
                    Order = _currentIndex++,
                    CanPlay = true
                });
            }
        }

        private async Task LoadArtistResult(int page)
        {
            var jv = await SimpleCacher.GetOrCreateCacheAsync(CacheType.Login, $"artist_sublist_{page}",
                    async () =>
                    {
                        return await LoadUserLibraryPageAsync(_knownTypeIds.ArtistTypeId, page * 25, 25);
                    });

            HasMore = jv.HasMore;
            foreach (var singerjson in jv.Items.OfType<ArtistBase>())
            {
                var translation = singerjson is IHasTranslation translationProvider ? translationProvider.Translation : null;
                var aliases = singerjson is IHasAliases aliasProvider ? aliasProvider.Aliases : null;
                Content.Add(new SimpleListItem
                {
                    Title = singerjson.Name,
                    LineOne = translation,
                    LineTwo = string.Join("/", aliases ?? []),
                    LineThree = string.Empty,
                    Route = new AppRoute.Artist($"{singerjson.ActualId}"),
                    PlayResource = new MusicResource.Artist($"{singerjson.ActualId}"),
                    CoverLink = await TryGetCoverLinkAsync(singerjson),
                    Order = _currentIndex++,
                    CanPlay = true
                });
            }
        }

        private async Task LoadAlbumResult(int page)
        {
            var json = await SimpleCacher.GetOrCreateCacheAsync(CacheType.Login, $"album_sublist_{page}",
                    async () =>
                    {
                        return await LoadUserLibraryPageAsync(_knownTypeIds.AlbumTypeId, page * 25, 25);
                    });

            HasMore = json.HasMore;
            foreach (var albumjson in json?.Items.OfType<AlbumBase>() ?? [])
            {
                var aliases = albumjson is IHasAliases aliasProvider ? aliasProvider.Aliases : null;
                var creators = albumjson is IHasCreators creatorsProvider ? await creatorsProvider.GetCreatorsAsync() : null;
                Content.Add(new SimpleListItem
                {
                    Title = albumjson.Name,
                    LineOne = string.Join(" / ", creators?.Select(creator => creator.Name) ?? []),
                    LineTwo = string.Join(" / ", aliases ?? []),
                    LineThree = string.Empty,
                    Route = new AppRoute.Album($"{albumjson.ActualId}"),
                    PlayResource = new MusicResource.Album($"{albumjson.ActualId}"),
                    CoverLink = await TryGetCoverLinkAsync(albumjson),
                    Order = _currentIndex++,
                    CanPlay = true
                });
            }
        }

        private async Task<UserLibraryPage> LoadUserLibraryPageAsync(string kind, int offset, int count)
        {
            if (await _userLibraryProvider.GetCurrentUserLibraryContainerAsync(kind) is not IProgressiveLoadingContainer container)
                return new UserLibraryPage();

            var (hasMore, items) = await container.GetProgressiveItemsListAsync(offset, count);
            return new UserLibraryPage
            {
                HasMore = hasMore,
                Items = items
            };
        }

        private static async Task<string?> TryGetCoverLinkAsync(ProvidableItemBase item)
        {
            if (item is not IHasCover coverProvider)
                return null;

            var result = await coverProvider.GetCoverAsync();
            return result is IResourceResultOf<System.Uri?> uriResult
                ? (await uriResult.GetResourceAsync())?.GetLeftPart(System.UriPartial.Path)
                : null;
        }

        private sealed class UserLibraryPage
        {
            public bool HasMore { get; init; }
            public List<ProvidableItemBase> Items { get; init; } = [];
        }

        public void OnSelectionChanged(NavigationViewItem item)
        {
            var tag = item.Tag as string;
            if (string.IsNullOrEmpty(tag))
                return;

            if (string.Equals(_currentTag, tag, System.StringComparison.Ordinal) && Content.Count > 0)
                return;

            CurrentPage = 0;
            _currentIndex = 1;
            _currentTag = tag;
            _loadedPages.Clear();
            _loadPageTask = null;
            Content.Clear();
            LoadCurrentPage().SafeFireAndForget();
        }

        [RelayCommand]
        private void LoadMore()
        {
            CurrentPage++;
            LoadCurrentPage().SafeFireAndForget();
        }

        private async Task LoadCurrentPage()
        {
            var page = CurrentPage;
            var tag = _currentTag;
            var pageKey = $"{tag}:{page}";
            if (!_loadedPages.Add(pageKey))
                return;

            if (_loadPageTask is { IsCompleted: false })
                await _loadPageTask;

            _loadPageTask = LoadPageContent(tag, page);
            await _loadPageTask;
        }
    }
}
