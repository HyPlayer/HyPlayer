using AsyncAwaitBestPractices;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HyPlayer.Classes;
using HyPlayer.NeteaseApi.ApiContracts;
using HyPlayer.NeteaseApi.ApiContracts.Album;
using HyPlayer.NeteaseApi.ApiContracts.Artist;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Windows.UI.Xaml.Controls;

namespace HyPlayer.ViewModels
{
    public partial class FavoriteViewModel : ObservableRecipient
    {
        public ObservableCollection<SimpleListItem> Content { get; set; } = new();
        [ObservableProperty]
        public partial int CurrentPage { get; set; }
        [ObservableProperty]
        public partial bool HasMore { get; set; }
        private int _currentIndex = 1;
        private string _currentTag;
        public async Task LoadPageContent(string tag)
        {
            switch (tag)
            {
                case "Album":
                    await LoadAlbumResult();
                    break;
                case "Artist":
                    await LoadArtistResult();
                    break;
                case "Radio":
                    await LoadRadioResult();
                    break;
            }
        }

        private async Task LoadRadioResult()
        {
            var jv = await SimpleCacher.GetOrCreateCacheAsync(CacheType.Login, $"djchannel_subscribed_{CurrentPage}",
                    async () =>
                    {
                        var json = await Common.NeteaseAPI.RequestAsync(NeteaseApis.DjChannelSubscribedApi);
                        if (json.IsError)
                        {
                            Common.AddToTeachingTipLists("加载订阅播客列表错误", json.Error.Message);
                            return null;
                        }

                        return json.Value;
                    });


            HasMore = jv.Data?.HasMore is true;
            foreach (var pljs in jv.Data?.Data ?? [])
            {
                Content.Add(new SimpleListItem
                {
                    Title = pljs.Name,
                    LineOne = pljs.UserName,
                    LineTwo = pljs.Description,
                    LineThree =
                        $"{DateConverter.FriendFormat(DateConverter.GetDateTimeFromTimeStamp(pljs.LastProgramCreateTime))}前 | 最后一个节目: " +
                        pljs.LastVoiceName,
                    ResourceId = "rd" + pljs.Id,
                    CoverLink = pljs.CoverUrl,
                    Order = _currentIndex++,
                    CanPlay = true
                });
            }
        }

        private async Task LoadArtistResult()
        {
            var jv = await SimpleCacher.GetOrCreateCacheAsync(CacheType.Login, $"artist_sublist_{CurrentPage}",
                    async () =>
                    {
                        var json = await Common.NeteaseAPI.RequestAsync(NeteaseApis.ArtistSublistApi,
                            new ArtistSublistRequest()
                            {
                                Limit = 25,
                                Offset = CurrentPage * 25
                            });
                        if (json.IsError)
                        {
                            Common.AddToTeachingTipLists("加载关注歌手列表错误", json.Error.Message);
                            return null;
                        }

                        return json.Value;
                    });

            HasMore = jv.HasMore;
            foreach (var singerjson in jv.Artists ?? [])
            {
                Content.Add(new SimpleListItem
                {
                    Title = singerjson.Name,
                    LineOne = singerjson.Translation,
                    LineTwo = string.Join("/", singerjson.Alias ?? []),
                    LineThree = $"专辑数 {singerjson.AlbumSize} | MV 数 {singerjson.MvSize}",
                    ResourceId = "ar" + singerjson.Id,
                    CoverLink = singerjson.Img1v1Url,
                    Order = _currentIndex++,
                    CanPlay = true
                });
            }
        }

        private async Task LoadAlbumResult()
        {
            var json = await SimpleCacher.GetOrCreateCacheAsync(CacheType.Login, $"album_sublist_{CurrentPage}",
                    async () =>
                    {
                        var jv = await Common.NeteaseAPI!.RequestAsync(NeteaseApis.AlbumSublistApi,
                            new AlbumSublistRequest()
                            {
                                Limit = 25,
                                Offset = CurrentPage * 25
                            });
                        if (jv.IsError)
                        {
                            Common.AddToTeachingTipLists("加载收藏专辑列表错误", jv.Error?.Message);
                            return null;
                        }

                        return jv.Value;
                    });

            HasMore = json.HasMore;
            foreach (var albumjson in json?.Data ?? [])
            {
                Content.Add(new SimpleListItem
                {
                    Title = albumjson.Name,
                    LineOne = string.Join(" / ", albumjson.Artists?.Select(t => t.Name) ?? []),
                    LineTwo = string.Join(" / ", albumjson.Alias ?? []),
                    LineThree = $"歌曲数:{albumjson.Size}",
                    ResourceId = "al" + albumjson.Id,
                    CoverLink = albumjson.PictureUrl,
                    Order = _currentIndex++,
                    CanPlay = true
                });
            }
        }

        public void OnSelectionChanged(NavigationViewItem item)
        {
            var tag = item.Tag as string;
            CurrentPage = 0;
            _currentIndex = 1;
            _currentTag = tag;
            Content.Clear();
            LoadPageContent(tag).SafeFireAndForget();
        }

        [RelayCommand]
        private void LoadMore()
        {
            CurrentPage++;
            LoadPageContent(_currentTag).SafeFireAndForget();
        }
    }
}
