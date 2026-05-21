using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.DependencyInjection;
using CommunityToolkit.Mvvm.Input;
using HyPlayer.Domain.Music;
using HyPlayer.Features.Playlist;
using HyPlayer.Infrastructure.Netease;
using HyPlayer.NeteaseApi;
using HyPlayer.NeteaseApi.ApiContracts;
using HyPlayer.Services.Abstractions;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace HyPlayer.Features.Home
{
    public partial class HomeViewModel : ObservableRecipient
    {
#nullable enable
        private NeteaseCloudMusicApiHandler _neteaseApi;
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
        public HomeViewModel(NeteaseCloudMusicApiHandler neteaseApi, IPlaylistService playlist, INavigationService navigation)
        {
            _neteaseApi = neteaseApi;
            _playlist = playlist;
            _navigation = navigation;
        }

        public async Task GetDataAsync()
        {
            var rcmdListResult = await _neteaseApi.RequestAsync(NeteaseApis.RecommendPlaylistsApi);
            var topListResult = await _neteaseApi.RequestAsync(NeteaseApis.ToplistApi);
            var categoryListResult = await _neteaseApi.RequestAsync(NeteaseApis.PlaylistCategoryListApi);
            var rcmdSongsResult = await _neteaseApi.RequestAsync(NeteaseApis.RecommendSongsApi);

            ToplistPlaylist = topListResult.IsSuccess ? topListResult.Value.List.Select(t => t.MapToNCPlayList()).ToList() : throw topListResult.Error;
            OfficialPlaylists = categoryListResult.IsSuccess ? categoryListResult.Value.Playlists.Select(t => t.MapToNCPlayList()).ToList() : throw categoryListResult.Error;
            // 登录内容
            if (Ioc.Default.GetRequiredService<IAuthService>().IsLoggedIn)
            {
                RecommendedPlaylist = rcmdListResult.IsSuccess ? rcmdListResult.Value.Recommends.Select(t => t.MapToNCPlayList()).ToList() : throw rcmdListResult.Error;
                RecommendedSongs = rcmdSongsResult.IsSuccess ? rcmdSongsResult.Value.Data.DailySongs.Select(t => t.MapNcSong()).ToList() : throw rcmdSongsResult.Error;
            }
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
