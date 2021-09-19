using HyPlayer.Classes;
using NeteaseCloudMusicApi;
using System;
using System.Collections.Generic;
using System.Linq;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;

// The Blank Page item template is documented at https://go.microsoft.com/fwlink/?LinkId=234238

namespace HyPlayer.Pages
{
    /// <summary>
    ///     An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class PageFavorite : Page, IDisposable
    {
        private int page;
        int i = 0;

        public PageFavorite()
        {
            InitializeComponent();
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            NavView.SelectedItem = NavView.MenuItems[0];
        }

        private void NavView_OnSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
        {
            page = 0;
            i = 0;
            ItemContainer.ListItems.Clear();
            RealLoad();
        }

        private void RealLoad()
        {
            switch ((NavView.SelectedItem as NavigationViewItem)?.Tag.ToString())
            {
                case "Album":
                    LoadAlbumResult();
                    break;
                case "Artist":
                    LoadArtistResult();
                    break;
                case "Radio":
                    LoadRadioResult();
                    break;
            }
        }

        private async void LoadRadioResult()
        {
            try
            {
                var json = await Common.ncapi.RequestAsync(CloudMusicApiProviders.DjSublist,
                    new Dictionary<string, object>
                    {
                        { "offset", page * 25 }
                    });
                BtnLoadMore.Visibility = json["hasMore"].ToObject<bool>() ? Visibility.Visible : Visibility.Collapsed;
                foreach (var pljs in json["djRadios"])
                {
                    ItemContainer.ListItems.Add(new SimpleListItem()
                    {
                        Title = pljs["name"].ToString(),
                        LineOne = pljs["dj"]["nickname"].ToString(),
                        LineTwo = pljs["desc"].ToString(),
                        LineThree = "最后一个节目: " + pljs["lastProgramName"].ToString(),
                        ResourceId = "rd" + json["id"],
                        CoverUri = pljs["picUrl"].ToString() + "?param=" + StaticSource.PICSIZE_SIMPLE_LINER_LIST_ITEM,
                        Order = i++
                    });
                }
            }
            catch (Exception ex)
            {
                Common.ShowTeachingTip(ex.Message, (ex.InnerException ?? new Exception()).Message);
            }
        }

        private async void LoadArtistResult()
        {
            try
            {
                var json = await Common.ncapi.RequestAsync(CloudMusicApiProviders.ArtistSublist,
                    new Dictionary<string, object>
                    {
                        { "offset", page * 25 }
                    });

                BtnLoadMore.Visibility = json["hasMore"].ToObject<bool>() ? Visibility.Visible : Visibility.Collapsed;
                foreach (var singerjson in json["data"])
                    ItemContainer.ListItems.Add(new SimpleListItem
                    {
                        Title = singerjson["name"].ToString(),
                        LineOne = singerjson["trans"].ToString(),
                        LineTwo = string.Join(" / ", singerjson["alia"]?.Select(t => t.ToString()) ?? new List<string>()),
                        LineThree = $"专辑数 {singerjson["albumSize"]} | MV 数 {singerjson["mvSize"]}",
                        ResourceId = "ar" + singerjson["id"],
                        CoverUri = singerjson["img1v1Url"].ToString() + "?param=" + StaticSource.PICSIZE_SIMPLE_LINER_LIST_ITEM,
                        Order = i++
                    });
            }
            catch (Exception ex)
            {
                Common.ShowTeachingTip(ex.Message, (ex.InnerException ?? new Exception()).Message);
            }
        }

        private async void LoadAlbumResult()
        {
            try
            {
                var json = await Common.ncapi.RequestAsync(CloudMusicApiProviders.AlbumSublist,
                    new Dictionary<string, object>
                    {
                        { "offset", page * 25 }
                    });
                BtnLoadMore.Visibility = json["hasMore"].ToObject<bool>() ? Visibility.Visible : Visibility.Collapsed;
                foreach (var albumjson in json["data"])
                {
                    ItemContainer.ListItems.Add(new SimpleListItem()
                    {
                        Title = albumjson["name"].ToString(),
                        LineOne = string.Join(" / ", albumjson["artists"].Select(t => t["name"].ToString())),
                        LineTwo = albumjson["alias"] != null
                            ? string.Join(" / ", albumjson["alias"].ToArray().Select(t => t.ToString()))
                            : "",
                        LineThree = albumjson.Value<bool>("paid") ? "付费专辑" : "",
                        ResourceId = "al" + albumjson["id"].ToString(),
                        CoverUri = albumjson["picUrl"].ToString() + "?param=" + StaticSource.PICSIZE_SIMPLE_LINER_LIST_ITEM,
                        Order = i++
                    });
                }
            }
            catch (Exception ex)
            {
                Common.ShowTeachingTip(ex.Message, (ex.InnerException ?? new Exception()).Message);
            }
        }

        private void BtnLoadMore_OnClick(object sender, RoutedEventArgs e)
        {
            page++;
            RealLoad();
        }

        public void Dispose()
        {
            ItemContainer.ListItems.Clear();
        }
    }
}