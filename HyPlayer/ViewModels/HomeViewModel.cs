using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HyPlayer.Classes;
using HyPlayer.Contracts.Services;
using HyPlayer.Contracts.ViewModels;
using HyPlayer.HyPlayControl;
using HyPlayer.Pages;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace HyPlayer.ViewModels
{
    public partial class HomeViewModel : ObservableRecipient, IViewModel
    {
#nullable enable
        private INeteaseProviderService _neteaseProviderService;

        [ObservableProperty] private List<NCPlayList> _recommendedPlaylist;
        [ObservableProperty] private List<NCPlayList> _toplistPlaylist;
        [ObservableProperty] private List<NCSong> _recommendedSongs;
        [ObservableProperty] private List<NCPlayList> _officialPlaylists;
#nullable restore
        public HomeViewModel(INeteaseProviderService neteaseProviderService)
        {
            _neteaseProviderService = neteaseProviderService;
        }

        public async Task GetDataAsync()
        {
            ToplistPlaylist = (await _neteaseProviderService.GetRecommendedResourceAsync(NeteaseTypeIds.Chart, default))
                .Select(t => (NCPlayList)t).ToList();

            OfficialPlaylists = (await _neteaseProviderService.GetRecommendedResourceAsync(NeteaseTypeIds.PlaylistCategory, default))
                .Select(t => (NCPlayList)t).ToList();

            // 登录内容
            if (_neteaseProviderService.IsLoggedIn)
            {
                RecommendedPlaylist = (await _neteaseProviderService.GetRecommendedResourceAsync(NeteaseTypeIds.Playlist, default))
                    .Select(t => (NCPlayList)t).ToList();

                RecommendedSongs = (await _neteaseProviderService.GetRecommendedResourceAsync(NeteaseTypeIds.SingleSong, default))
                    .Select(t => (NCSong)t).ToList();
            }
        }

        [RelayCommand]
        private void OnLikedClicked()
        {
            Common.NavigatePage(typeof(SongListDetail), Common.MySongLists[0].plid);
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
    }
}
