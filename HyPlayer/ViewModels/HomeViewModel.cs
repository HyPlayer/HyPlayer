using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HyPlayer.Classes;
using HyPlayer.HyPlayControl;
using HyPlayer.NeteaseApi;
using HyPlayer.NeteaseApi.ApiContracts;
using HyPlayer.Pages;
using Newtonsoft.Json.Linq;
using NMeCab.Core;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Windows.Foundation.Metadata;

namespace HyPlayer.ViewModels
{
    public partial class HomeViewModel : ObservableRecipient
    {
#nullable enable
        private NeteaseCloudMusicApiHandler _neteaseApi;

        [ObservableProperty] 
        public partial List<NCPlayList> RecommendedPlaylist { get; set; }
        [ObservableProperty] 
        public partial List<NCPlayList> ToplistPlaylist { get; set; }
        [ObservableProperty]
        public partial List<NCSong> RecommendedSongs { get; set; }
        [ObservableProperty] 
        public partial List<NCPlayList> OfficialPlaylists { get; set; }
#nullable restore
        public HomeViewModel(NeteaseCloudMusicApiHandler neteaseApi)
        {
            _neteaseApi = neteaseApi;
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
            if (Common.Logined)
            {
                RecommendedPlaylist = rcmdListResult.IsSuccess ? rcmdListResult.Value.Recommends.Select(t => t.MapToNCPlayList()).ToList() : throw rcmdListResult.Error;
                RecommendedSongs = rcmdSongsResult.IsSuccess ? rcmdSongsResult.Value.Data.DailySongs.Select(t => t.MapNcSong()).ToList() : throw rcmdSongsResult.Error;
            }
        }

        [RelayCommand]
        private void OnLikedClicked()
        {
            Common.NavigatePage(typeof(SongListDetail), Common.MySongLists[0].PlaylistId);
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
        private void OnPlayAllRecommendedSongsClicked()
        {
            HyPlayList.RemoveAllSong();
            HyPlayList.AppendNcSongs(RecommendedSongs);
            HyPlayList.NowPlaying = -1;
            HyPlayList.SongMoveNext();
        }
    }
}
