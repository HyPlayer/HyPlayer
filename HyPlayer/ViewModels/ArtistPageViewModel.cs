using AsyncAwaitBestPractices;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HyPlayer.Classes;
using HyPlayer.NeteaseApi.ApiContracts;
using HyPlayer.NeteaseApi.ApiContracts.Artist;
using HyPlayer.NeteaseApi.ApiContracts.Song;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Windows.UI.Xaml.Media.Imaging;

namespace HyPlayer.ViewModels
{
    public partial class ArtistPageViewModel : ObservableRecipient
    {
        public ObservableCollection<NCSong> AllSongs { get; set; } = [];
        public ObservableCollection<NCSong> HotSongs { get; set; } = [];
        public ObservableCollection<SimpleListItem> Albums { get; set; } = [];
        [ObservableProperty]
        public partial NCArtist Artist { get; set; }
        [ObservableProperty]
        public partial int CurrentPage { get; set; } = 0;
        [ObservableProperty]
        public partial int CurrentPivotIndex { get; set; } = 0;
        [ObservableProperty]
        public partial bool HasNextPage { get; set; }
        [ObservableProperty]
        public partial bool HasPreviousPage { get; set; }
        [ObservableProperty]
        public partial BitmapImage Image { get; set; }

        public async Task InitializeArtistInfo(string artistId)
        {
            try
            {
                if (artistId is null)
                {
                    Common.AddToTeachingTipLists("艺人ID为空", "请检查传入的参数是否正确");
                    return;
                }
                var res = await SimpleCacher.GetOrCreateCacheAsync(CacheType.ArtistDetail, artistId, async () =>
                {
                    var resp = await Common.NeteaseAPI!.RequestAsync(NeteaseApis.ArtistDetailApi,
                        new ArtistDetailRequest() { ArtistId = artistId });
                    if (resp.IsError && resp.Error?.ErrorCode.ToString() == "404")
                    {
                        Common.AddToTeachingTipLists("艺人不存在", null);
                        return null;
                    }
                    if (resp.IsError)
                    {
                        Common.AddToTeachingTipLists("获取艺人信息失败", resp.Error?.Message);
                        return null;
                    }

                    return resp.Value;
                });

                if (res is null)
                {
                    return;
                }

                Artist = res?.Artist.MapToNcArtist();
                if (res?.Artist?.PicUrl?.StartsWith("http") is true)
                {
                    if (Common.Setting.noImage)
                    {
                        Image = null;
                    }

                    BitmapImage image = new BitmapImage(new Uri(res.Artist.PicUrl + "?param=" + StaticSource.PICSIZE_ARTIST_DETAIL_COVER));
                    Image = image;
                }
                LoadHotSongs().SafeFireAndForget();
                LoadSongs().SafeFireAndForget();
                LoadAlbum().SafeFireAndForget();
            }
            catch (Exception ex)
            {
                Common.AddToTeachingTipLists(ex.Message, (ex.InnerException ?? new Exception()).Message);
            }
        }
        private async Task LoadHotSongs()
        {
            try
            {
                HotSongs.Clear();
                var j1 = await SimpleCacher.GetOrCreateCacheAsync(CacheType.ArtistTopSongsDetail, Artist.Id, async () =>
                {
                    var j1res = await Common.NeteaseAPI!.RequestAsync(NeteaseApis.ArtistTopSongApi,
                        new ArtistTopSongRequest() { ArtistId = Artist.Id });
                    if (j1res.IsError)
                    {
                        Common.AddToTeachingTipLists("获取歌手热门歌曲失败", j1res.Error?.Message);
                        return null;
                    }

                    return j1res.Value?.Songs;
                });
                var idx = 0;
                var jv = await SimpleCacher.GetOrCreateCacheAsync(CacheType.SongDetail, Artist.Id, async () =>
                {
                    var json = await Common.NeteaseAPI!.RequestAsync(NeteaseApis.SongDetailApi,
                        new SongDetailRequest() { IdList = j1?.Select(t => t.Id).ToList() });
                    if (json.IsError)
                    {
                        Common.AddToTeachingTipLists("获取歌手歌曲信息失败", json.Error.Message);
                        return null;
                    }

                    return json.Value;
                });
                if (jv is null)
                {
                    return;
                }

                foreach (var item in jv?.Songs ?? [])
                {
                    var ncSong = item.MapToNcSong();
                    ncSong.Order = idx++;
                    HotSongs.Add(ncSong);
                }
            }
            catch (Exception ex)
            {
                if (ex.GetType() != typeof(TaskCanceledException) && ex.GetType() != typeof(OperationCanceledException))
                    Common.AddToTeachingTipLists(ex.Message, (ex.InnerException ?? new Exception()).Message);
            }
        }

        private async Task LoadSongs()
        {
            try
            {
                var j1 = await SimpleCacher.GetOrCreateCacheAsync(CacheType.ArtistSongsDetial, Artist.Id + "_" + CurrentPage,
                    async () =>
                    {
                        var resp = await Common.NeteaseAPI!.RequestAsync(NeteaseApis.ArtistSongsApi,
                            new ArtistSongsRequest() { ArtistId = Artist.Id, Limit = 50, Offset = CurrentPage * 50 });
                        if (resp.IsError)
                        {
                            Common.AddToTeachingTipLists("获取歌手歌曲失败", resp.Error?.Message);
                            return null;
                        }

                        return resp.Value;
                    });
                var idx = 0;
                foreach (var item in j1.Songs)
                {
                    var ncSong = item.MapNcSong();
                    ncSong.IsAvailable = item.Privilege.St == 0;
                    ncSong.Order = CurrentPage * 50 + idx++;
                    AllSongs.Add(ncSong);
                }
                HasNextPage = j1.HasMore;
            }
            catch (Exception ex)
            {
                if (ex.GetType() != typeof(TaskCanceledException) && ex.GetType() != typeof(OperationCanceledException))
                    Common.AddToTeachingTipLists(ex.Message, (ex.InnerException ?? new Exception()).Message);
            }
        }
        private async Task LoadAlbum()
        {
            try
            {
                Albums.Clear();
                var jv = await SimpleCacher.GetOrCreateCacheAsync(CacheType.ArtistAlbumsList, Artist.Id + "_" + CurrentPage,
                    async () =>
                    {
                        var resp = await Common.NeteaseAPI!.RequestAsync(NeteaseApis.ArtistAlbumsApi,
                            new ArtistAlbumsRequest() { ArtistId = Artist.Id, Limit = 50, Start = CurrentPage * 50 });
                        if (resp.IsError)
                        {
                            Common.AddToTeachingTipLists("获取歌手专辑失败", resp.Error?.Message);
                            return null;
                        }

                        return resp.Value;
                    });

                var i = 0;
                foreach (var album in jv?.Albums ?? [])
                {
                    Albums.Add(new SimpleListItem
                    {
                        Title = album.Name,
                        LineOne = string.Join("/", album.Artists?.Select(t => t.Name) ?? []),
                        LineTwo = album.Alias != null
                            ? string.Join(" / ", album.Alias)
                            : "",
                        LineThree = album.Paid ? "付费专辑" : "",
                        ResourceId = "al" + album.Id,
                        CoverLink = album.PictureUrl,
                        Order = CurrentPage * 50 + i++,
                        CanPlay = true
                    });
                }
                HasNextPage = jv?.HasMore ?? false;
                HasPreviousPage = CurrentPage > 0;
            }
            catch (TaskCanceledException)
            {
                //Ignore
            }
            catch (OperationCanceledException)
            {
                //Ignore
            }
            catch (Exception ex)
            {
                Common.AddToTeachingTipLists(ex.Message, (ex.InnerException ?? new Exception()).Message);
            }
        }
        [RelayCommand]
        private void NextPage()
        {
            CurrentPage++;
            if (CurrentPivotIndex == 1)
                LoadSongs().SafeFireAndForget();
            else if (CurrentPivotIndex == 2)
                LoadAlbum().SafeFireAndForget();
        }
        [RelayCommand]
        private void PreviousPage()
        {
            CurrentPage--;
            if (CurrentPivotIndex == 1)
                LoadSongs().SafeFireAndForget();
            else if (CurrentPivotIndex == 2)
                LoadAlbum().SafeFireAndForget();
        }
    }
}
