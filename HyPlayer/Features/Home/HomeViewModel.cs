using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.DependencyInjection;
using CommunityToolkit.Mvvm.Input;
using HyPlayer.Domain.Music;
using HyPlayer.Features.Playlist;
using HyPlayer.Infrastructure.Netease;
using HyPlayer.NeteaseProvider.Models;
using HyPlayer.PlayCore.Abstraction.Interfaces.PlayListContainer;
using HyPlayer.PlayCore.Abstraction.Models;
using HyPlayer.PlayCore.Abstraction.Models.Containers;
using HyPlayer.PlayCore.Abstraction.Models.SingleItems;
using HyPlayer.Services.Abstractions;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace HyPlayer.Features.Home
{
    public partial class HomeViewModel : ObservableRecipient
    {
#nullable enable
        private readonly global::HyPlayer.NeteaseProvider.NeteaseProvider _neteaseProvider;
        private readonly IPlaylistService _playlist;
        private readonly INavigationService _navigation;

        [ObservableProperty]
        public partial List<NCPlayList> RecommendedPlaylist { get; set; }
        [ObservableProperty]
        public partial List<NCPlayList> ToplistPlaylist { get; set; }
        [ObservableProperty]
        public partial List<NCSong> RecommendedSongs { get; set; }
        [ObservableProperty]
        public partial List<NCPlayList> OfficialPlaylists { get; set; }
#nullable restore
        public HomeViewModel(global::HyPlayer.NeteaseProvider.NeteaseProvider neteaseProvider, IPlaylistService playlist, INavigationService navigation)
        {
            _neteaseProvider = neteaseProvider;
            _playlist = playlist;
            _navigation = navigation;
        }

        public async Task GetDataAsync()
        {
            ToplistPlaylist = (await LoadContainerItemsAsync(new NeteaseToplistContainer { ActualId = "chart", Name = "排行榜" }))
                .OfType<NeteasePlaylist>()
                .Select(MapToNCPlayList)
                .ToList();
            OfficialPlaylists = (await LoadContainerItemsAsync(new NeteasePlaylistCategoryContainer { ActualId = "官方", Category = "官方", Name = "官方推荐歌单" }))
                .OfType<NeteasePlaylist>()
                .Select(MapToNCPlayList)
                .ToList();
            // 登录内容
            if (Ioc.Default.GetRequiredService<IAuthService>().IsLoggedIn)
            {
                RecommendedPlaylist = (await LoadContainerItemsAsync(new NeteaseRecommendPlaylistContainer { ActualId = "rcpl", Name = "推荐歌单" }))
                    .OfType<NeteasePlaylist>()
                    .Select(MapToNCPlayList)
                    .ToList();
                RecommendedSongs = (await LoadContainerItemsAsync(new NeteaseRecommendSongContainer { ActualId = "rcsg", Name = "推荐歌曲" }))
                    .OfType<SingleSongBase>()
                    .Select(song => song.ToHyPlayItem().ToNCSong())
                    .ToList();
            }
        }

        private static async Task<List<ProvidableItemBase>> LoadContainerItemsAsync(ContainerBase container)
        {
            return container switch
            {
                IProgressiveLoadingContainer progressive => (await progressive.GetProgressiveItemsListAsync(0, progressive.MaxProgressiveCount)).Item2,
                LinerContainerBase liner => await liner.GetAllItemsAsync(),
                UndeterminedContainerBase undetermined => await undetermined.GetNextItemsRangeAsync(),
                _ => []
            };
        }

        private static NCPlayList MapToNCPlayList(NeteasePlaylist playlist)
        {
            return new NCPlayList
            {
                PlaylistId = playlist.ActualId ?? string.Empty,
                Name = playlist.Name,
                Description = playlist.Description,
                Cover = playlist.CoverUrl,
                Creator = playlist.Creator is null
                    ? null
                    : new NCUser
                    {
                        Id = playlist.Creator.ActualId ?? string.Empty,
                        Name = playlist.Creator.Name,
                        Avatar = string.Empty,
                        Signature = string.Empty
                    },
                HasSubscribed = playlist.Subscribed,
                TrackCount = playlist.TrackCount,
                PlayCount = playlist.PlayCount,
                BookCount = playlist.SubscribedCount,
                UpdateTime = playlist.UpdateTime > 0 ? System.DateTimeOffset.FromUnixTimeMilliseconds(playlist.UpdateTime).LocalDateTime : System.DateTime.MinValue
            };
        }

        [RelayCommand]
        private void OnLikedClicked()
        {
            _navigation.Navigate(typeof(SongListDetail), Ioc.Default.GetRequiredService<IAuthService>().MySongLists[0].PlaylistId);
        }

        [RelayCommand]
        private void OnHeartBeatModeClicked()
        {
            _ = Api.EnterIntelligencePlay(new System.Threading.CancellationToken());
        }

        [RelayCommand]
        private void OnPersonalFmClicked()
        {
            PersonalFM.InitPersonalFM();
        }

        [RelayCommand]
        private async Task OnPlayAllRecommendedSongsClickedAsync()
        {
            var items = RecommendedSongs.Select(s => s.ToHyPlayItem());
            _playlist.AppendItems(items, true);
            _playlist.NotifyAppendDone();
            await _playlist.MoveNextAsync(userInitiated: true);
        }
    }
}
