using AsyncAwaitBestPractices;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HyPlayer.Classes;
using HyPlayer.HyPlayControl;
using HyPlayer.NeteaseApi.ApiContracts;
using HyPlayer.NeteaseApi.ApiContracts.Album;
using HyPlayer.Pages;
using HyPlayer.Services.Abstractions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Media.Imaging;

namespace HyPlayer.ViewModels
{
    public partial class AlbumPageViewModel :ObservableRecipient
    {
        private readonly IPlaylistService _playlist;

        public AlbumPageViewModel(IPlaylistService playlist)
        {
            _playlist = playlist;
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
        public partial string SourceId { get; set; }
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
                var json = await Common.NeteaseAPI!.RequestAsync(NeteaseApis.AlbumDetailDynamicApi,
                    new AlbumDetailDynamicRequest() { Id = albumId });
                if (json.IsError)
                {
                    Common.AddToTeachingTipLists("获取专辑动态失败", json.Error?.Message);
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
                    var json = await Common.NeteaseAPI!.RequestAsync(NeteaseApis.AlbumApi,
                        new AlbumRequest() { Id = albumId });
                    if (json.IsError)
                    {
                        Common.AddToTeachingTipLists("获取专辑信息失败", json.Error?.Message);
                        return null;
                    }

                    return json.Value;
                });
                if (rst?.Album is null)
                {
                    return;
                }

                Album = rst.Album.MapToNcAlbum();
                if (!Common.Setting.noImage) SourceImage = new BitmapImage(new Uri(Album.Cover + "?param=" + StaticSource.PICSIZE_PLAYLIST_ITEM_COVER));
                else SourceImage = new BitmapImage(new Uri("/Assets/icon.png"));

                var artists = rst.Album.Artists?.Select(t => t.MapToNcArtist()).ToList();
                AuthorString = string.Join(" / ", artists?.Select(t => t.Name) ?? []);
                Description = (string.Join(" / ", rst.Album.Alias) + rst.Album.Alias != null ? "\r\n" : string.Empty) + rst.Album.Description;
                var idx = 0;
                SourceId = "al" + Album.Id;
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
                Common.AddToTeachingTipLists(ex.Message, (ex.InnerException ?? new Exception()).Message);
            }
        }
        [RelayCommand]
        public async Task PlayAll()
        {
            try
            {
                _playlist.Clear();
                // TODO: Migrate AppendNcSource to IPlaylistService once API-loading logic is extracted
                await _playlist.AppendNcSourceAsync("al" + Album.Id);
                _playlist.PlaySourceId = "al" + Album.Id;
                await _playlist.MoveToAsync(_playlist.Items.FirstOrDefault());
            }
            catch (Exception ex)
            {
                Common.AddToTeachingTipLists(ex.Message, (ex.InnerException ?? new Exception()).Message);
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
        private void NavigateComment()
        {
            Common.NavigatePage(typeof(Comments), "al" + Album.Id);
        }

        [RelayCommand]
        private void Subscribe()
        {
            _ = Common.NeteaseAPI?.RequestAsync(NeteaseApis.AlbumSubscribeApi,
                new AlbumSubscribeRequest() { Id = Album.Id, IsSubscribe = !Subscribed });
            Subscribed = !Subscribed;
        }

        [RelayCommand]
        private void AddAllToPlaylist()
        {
            // TODO: Migrate AppendNcSource to IPlaylistService once API-loading logic is extracted
            _playlist.AppendNcSourceAsync("al" + Album.Id).SafeFireAndForget();
        }
    }
}
