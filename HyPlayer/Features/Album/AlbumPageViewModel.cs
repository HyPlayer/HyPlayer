using AsyncAwaitBestPractices;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HyPlayer.Domain;
using HyPlayer.Domain.Music;
using HyPlayer.Domain.Settings;
using HyPlayer.Infrastructure.Netease;
using HyPlayer.NeteaseApi;
using HyPlayer.NeteaseApi.ApiContracts;
using HyPlayer.NeteaseApi.ApiContracts.Album;
using HyPlayer.Services.Abstractions;
using HyPlayer.Services.Cache;
using HyPlayer.Services.Downloads;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Media.Imaging;

namespace HyPlayer.Features.Album
{
    public partial class AlbumPageViewModel : ObservableRecipient
    {
        private readonly IPlaylistService _playlist;
        private readonly NeteaseCloudMusicApiHandler _api;
        private readonly Setting _setting;
    private readonly ITeachingTipService _teachingTipService;
        private readonly INavigationService _navigation;
        private readonly IAppNavigator _navigator;
        private readonly IBackgroundTaskRunner _taskRunner;

        public AlbumPageViewModel(
            IPlaylistService playlist,
            NeteaseCloudMusicApiHandler api,
            Setting setting,
            ITeachingTipService teachingTip,
            INavigationService navigation,
            IAppNavigator navigator,
            IBackgroundTaskRunner taskRunner)
        {
            _playlist = playlist;
            _api = api;
            _setting = setting;
            _teachingTipService = teachingTip;
            _navigation = navigation;
            _navigator = navigator;
            _taskRunner = taskRunner;
            QueueScope = SongListQueueScope.Visible;
        }

        [ObservableProperty]
        public partial NCAlbum Album { get; set; }
        [ObservableProperty]
        public partial CollectionViewSource AlbumSongsViewSource { get; set; }
        [ObservableProperty]
        public partial List<NCArtist> Artists { get; set; }
        [ObservableProperty]
        public partial string AuthorString { get; set; }
        [ObservableProperty]
        public partial string Description { get; set; }
        [ObservableProperty]
        public partial SongListQueueScope QueueScope { get; set; }
        [ObservableProperty]
        public partial bool Subscribed { get; set; }
        [ObservableProperty]
        public partial BitmapImage SourceImage { get; set; }
        [ObservableProperty]
        public partial long PublishTime { get; set; }
        public async Task LoadAlbumDynamic(string albumId)
        {
            var js = await SimpleCacher.GetOrCreateCacheAsync(CacheType.AlbumDynamic, albumId, async () =>
            {
                var json = await _api.RequestAsync(NeteaseApis.AlbumDetailDynamicApi,
                    new AlbumDetailDynamicRequest() { Id = albumId });
                if (json.IsError)
                {
                    _teachingTipService.Enqueue(new("获取专辑动态失败", json.Error?.Message));
                    return null;
                }

                return json.Value;
            });
            Subscribed = js.IsSub;
        }

        public async Task LoadAlbumInfo(string albumId)
        {
            try
            {
                var rst = await SimpleCacher.GetOrCreateCacheAsync(CacheType.AlbumInfo, albumId, async () =>
                {
                    var json = await _api.RequestAsync(NeteaseApis.AlbumApi,
                        new AlbumRequest() { Id = albumId });
                    if (json.IsError)
                    {
                        _teachingTipService.Enqueue(new("获取专辑信息失败", json.Error?.Message));
                        return null;
                    }

                    return json.Value;
                });
                if (rst?.Album is null)
                {
                    return;
                }

                Album = rst.Album.MapToNcAlbum();
                if (!_setting.noImage) SourceImage = new BitmapImage(new Uri(Album.Cover + "?param=" + StaticSource.PICSIZE_PLAYLIST_ITEM_COVER));
                else SourceImage = new BitmapImage(new Uri("/Assets/icon.png"));

                var artists = rst.Album.Artists?.Select(t => t.MapToNcArtist()).ToList();
                AuthorString = string.Join(" / ", artists?.Select(t => t.Name) ?? []);
                Description = (string.Join(" / ", rst.Album.Alias) + rst.Album.Alias != null ? "\r\n" : string.Empty) + rst.Album.Description;
                var idx = 0;
                QueueScope = SongListQueueScope.Album(Album.Id);
                PublishTime = rst.Album.PublishTime;
                AlbumSongsViewSource = new CollectionViewSource()
                {
                    IsSourceGrouped = true,
                    Source = rst.Songs?.Select(song =>
                    {
                        return new NCAlbumSong
                        {
                            Album = song.Album.MapToNcAlbum(),
                            Alias = song.Alias is not null ? string.Join(",", song.Alias) : null,
                            Artist = song.Artists?.Select(artist => artist.MapToNcArtist())
                                         .ToList() ??
                                     [],
                            DiscName = song.CdName,
                            CDName = song.CdName,
                            IsCloud = song.Sid is not "0",
                            IsVip = song.Fee is 1,
                            LengthInMilliseconds = song.Duration,
                            MVId = song.MvId,
                            SongId = song.Id,
                            Order = ++idx,
                            SongName = song.Name,
                            TrackId = song.TrackNumber,
                            TranslatedName = song.Translations is not null ? string.Join(",", song.Translations) : null,
                            IsAvailable = true,
                            Type = HyPlayItemType.Netease,
                        };
                    }).GroupBy(t => t.DiscName).OrderBy(t => t.Key)
                    .Select(t => new DiscSongs(t) { Key = t.Key }).ToList()
                };
            }
            catch (Exception ex)
            {
                _teachingTipService.Enqueue(new(ex.Message, ex.InnerException.Message));
            }
        }
        [RelayCommand]
        public async Task PlayAll()
        {
            try
            {
                _playlist.Clear();
                await _navigator.AppendAsync(new MusicResource.Album(Album.Id));
                await _playlist.MoveToAsync(_playlist.Items.FirstOrDefault());
            }
            catch (Exception ex)
            {
                _teachingTipService.Enqueue(new("获取专辑信息失败", (ex.InnerException ?? new Exception()).Message));
            }
        }

        [RelayCommand]
        private void DownloadAll()
        {
            var songs = new List<NCSong>();
            foreach (var discSongs in (IEnumerable<DiscSongs>)AlbumSongsViewSource.Source) songs.AddRange(discSongs);

            DownloadManager.AddDownload(songs);
        }

        [RelayCommand]
        private void Subscribe()
        {
            _taskRunner.Forget(_api.RequestAsync(NeteaseApis.AlbumSubscribeApi,
                new AlbumSubscribeRequest() { Id = Album.Id, IsSubscribe = !Subscribed }),
                "toggle album subscription");
            Subscribed = !Subscribed;
        }

        [RelayCommand]
        private void AddAllToPlaylist()
        {
            _navigator.AppendAsync(new MusicResource.Album(Album.Id)).SafeFireAndForget();
        }
    }
}
