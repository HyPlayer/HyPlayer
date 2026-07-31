using AsyncAwaitBestPractices;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HyPlayer.Domain.Music;
using HyPlayer.PlayCore.Abstraction.Interfaces.ProvidableItem;
using HyPlayer.PlayCore.Abstraction.Interfaces.PlayListContainer;
using HyPlayer.PlayCore.Abstraction.Interfaces.Provider;
using HyPlayer.PlayCore.Abstraction.Models;
using HyPlayer.PlayCore.Abstraction.Models.Containers;
using HyPlayer.Application.Diagnostics;
using HyPlayer.Application.Notifications;
using HyPlayer.Application.State;
using HyPlayer.Features.Account.Services;
using HyPlayer.Features.Downloads.Services;
using HyPlayer.Features.History.Services;
using HyPlayer.Features.LastFM.Services;
using HyPlayer.Features.Lyrics.Services;
using HyPlayer.Features.Playback.QueueProviders;
using HyPlayer.Features.Playback.Services;
using HyPlayer.Features.Widgets.Services;
using HyPlayer.Platform.Runtime;
using HyPlayer.Platform.Runtime.Background;
using HyPlayer.Platform.Storage;
using HyPlayer.Platform.SystemServices;
using HyPlayer.Platform.Tiles;
using HyPlayer.Shell.Navigation.Services;
using HyPlayer.Shell.Playback;
using HyPlayer.Shell.Services;
using HyPlayer.UI.Playback.PlayBar;
using HyPlayer.UI.TeachingTips;
using HyPlayer.Platform.Storage.Cache;
using HyPlayer.UI.Lists;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Windows.UI.Xaml.Controls;

namespace HyPlayer.Features.Library
{
    public partial class FavoriteViewModel(
        IUserLibraryProvidable userLibraryProvider,
        IProviderKnownTypeIds knownTypeIds) : ObservableObject
    {
        private readonly ObservableCollection<ProvidableItemBase> _content = [];
        [ObservableProperty]
        public partial int CurrentPage { get; set; }
        [ObservableProperty]
        public partial bool HasMore { get; set; }
        [ObservableProperty]
        public partial ContainerBase ContentContainer { get; set; }
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
                        return await LoadUserLibraryPageAsync(knownTypeIds.RadioChannelTypeId!, page * 200, 200);
                    });


            HasMore = jv.HasMore;
            foreach (var item in jv.Items.OfType<LinerContainerBase>())
            {
                _content.Add(item);
            }
            RefreshContentContainer();
        }

        private async Task LoadArtistResult(int page)
        {
            var jv = await SimpleCacher.GetOrCreateCacheAsync(CacheType.Login, $"artist_sublist_{page}",
                    async () =>
                    {
                        return await LoadUserLibraryPageAsync(knownTypeIds.ArtistTypeId, page * 25, 25);
                    });

            HasMore = jv.HasMore;
            foreach (var singerjson in jv.Items.OfType<ArtistBase>())
            {
                _content.Add(singerjson);
            }
            RefreshContentContainer();
        }

        private async Task LoadAlbumResult(int page)
        {
            var json = await SimpleCacher.GetOrCreateCacheAsync(CacheType.Login, $"album_sublist_{page}",
                    async () =>
                    {
                        return await LoadUserLibraryPageAsync(knownTypeIds.AlbumTypeId, page * 25, 25);
                    });

            HasMore = json.HasMore;
            foreach (var albumjson in json?.Items.OfType<AlbumBase>() ?? [])
            {
                _content.Add(albumjson);
            }
            RefreshContentContainer();
        }

        private async Task<UserLibraryPage> LoadUserLibraryPageAsync(string kind, int offset, int count)
        {
            if (await userLibraryProvider.GetCurrentUserLibraryContainerAsync(kind) is not IProgressiveLoadingContainer container)
                return new UserLibraryPage();

            var (hasMore, items) = await container.GetProgressiveItemsListAsync(offset, count);
            return new UserLibraryPage
            {
                HasMore = hasMore,
                Items = items
            };
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

            if (string.Equals(_currentTag, tag, System.StringComparison.Ordinal) && _content.Count > 0)
                return;

            CurrentPage = 0;
            _currentTag = tag;
            _loadedPages.Clear();
            _loadPageTask = null;
            _content.Clear();
            RefreshContentContainer();
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

        private void RefreshContentContainer()
        {
            ContentContainer = new StaticItemsContainer(_content, "收藏", _currentTag ?? "favorite");
        }
    }
}
