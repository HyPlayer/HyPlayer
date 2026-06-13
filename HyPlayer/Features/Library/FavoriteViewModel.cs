using AsyncAwaitBestPractices;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HyPlayer.Domain.Music;
using HyPlayer.Domain.Navigation;
using HyPlayer.NeteaseProvider.Constants;
using HyPlayer.NeteaseProvider.Models;
using HyPlayer.PlayCore.Abstraction.Interfaces.PlayListContainer;
using HyPlayer.PlayCore.Abstraction.Models;
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
        private readonly global::HyPlayer.NeteaseProvider.NeteaseProvider _userLibraryProvider;
        private readonly INotificationService _notification;

        public FavoriteViewModel(
            global::HyPlayer.NeteaseProvider.NeteaseProvider userLibraryProvider,
            INotificationService notification)
        {
            _userLibraryProvider = userLibraryProvider;
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
                        return await LoadUserLibraryPageAsync(NeteaseTypeIds.RadioChannel, page * 200, 200);
                    });


            HasMore = jv.HasMore;
            foreach (var item in jv.Items.OfType<NeteaseRadioChannel>())
            {
                Content.Add(new SimpleListItem
                {
                    Title = item.Name,
                    LineOne = string.Join(" / ", item.CreatorList ?? []),
                    LineTwo = item.Description,
                    LineThree =
                        $"{DateConverter.FriendFormat(DateConverter.GetDateTimeFromTimeStamp(item.LastProgramCreateTime))}前 | 最后一个节目: " +
                        item.LastProgramName,
                    Route = new AppRoute.Radio($"{item.ActualId}"),
                    PlayResource = new MusicResource.Radio($"{item.ActualId}"),
                    CoverLink = item.CoverUrl,
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
                        return await LoadUserLibraryPageAsync(NeteaseTypeIds.Artist, page * 25, 25);
                    });

            HasMore = jv.HasMore;
            foreach (var singerjson in jv.Items.OfType<NeteaseArtist>())
            {
                Content.Add(new SimpleListItem
                {
                    Title = singerjson.Name,
                    LineOne = singerjson.Translation,
                    LineTwo = string.Join("/", singerjson.Alias ?? []),
                    LineThree = $"专辑数 {singerjson.AlbumSize} | MV 数 {singerjson.MvSize}",
                    Route = new AppRoute.Artist($"{singerjson.ActualId}"),
                    PlayResource = new MusicResource.Artist($"{singerjson.ActualId}"),
                    CoverLink = singerjson.CoverUrl,
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
                        return await LoadUserLibraryPageAsync(NeteaseTypeIds.Album, page * 25, 25);
                    });

            HasMore = json.HasMore;
            foreach (var albumjson in json?.Items.OfType<NeteaseAlbum>() ?? [])
            {
                Content.Add(new SimpleListItem
                {
                    Title = albumjson.Name,
                    LineOne = string.Join(" / ", albumjson.CreatorList ?? []),
                    LineTwo = string.Join(" / ", albumjson.Alias ?? []),
                    LineThree = albumjson.SubType,
                    Route = new AppRoute.Album($"{albumjson.ActualId}"),
                    PlayResource = new MusicResource.Album($"{albumjson.ActualId}"),
                    CoverLink = albumjson.PictureUrl,
                    Order = _currentIndex++,
                    CanPlay = true
                });
            }
        }

        private static async Task<UserLibraryPage> LoadUserLibraryPageAsync(string kind, int offset, int count)
        {
            var container = new NeteaseUserLibrarySubContainer
            {
                ActualId = $"library-{kind}",
                Name = "用户资料库",
                Kind = kind,
                MaxProgressiveCount = count
            };
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
