using System;
using System.Collections.Generic;
using System.Linq;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;
using HyPlayer.Classes;
using HyPlayer.Controls;
using NeteaseCloudMusicApi;

// The Blank Page item template is documented at https://go.microsoft.com/fwlink/?LinkId=234238

namespace HyPlayer.Pages
{
    /// <summary>
    ///     An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class PageFavorite : Page,IDisposable
    {
        private int page;

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
            ItemContainer.Children.Clear();
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
            var (isok, json) = await Common.ncapi.RequestAsync(CloudMusicApiProviders.DjSublist,
                new Dictionary<string, object>
                {
                    {"offset", page * 25}
                });
            if (isok)
            {
                BtnLoadMore.Visibility = json["hasMore"].ToObject<bool>() ? Visibility.Visible : Visibility.Collapsed;
                foreach (var token in json["djRadios"])
                    ItemContainer.Children.Add(new SingleRadio(NCRadio.CreateFromJson(token)));
            }
        }

        private async void LoadArtistResult()
        {
            var (isok, json) = await Common.ncapi.RequestAsync(CloudMusicApiProviders.ArtistSublist,
                new Dictionary<string, object>
                {
                    {"offset", page * 25}
                });
            if (isok)
            {
                BtnLoadMore.Visibility = json["hasMore"].ToObject<bool>() ? Visibility.Visible : Visibility.Collapsed;
                foreach (var token in json["data"])
                    ItemContainer.Children.Add(new SingleArtist(NCArtist.CreateFromJson(token)));
            }
        }

        private async void LoadAlbumResult()
        {
            var (isok, json) = await Common.ncapi.RequestAsync(CloudMusicApiProviders.AlbumSublist,
                new Dictionary<string, object>
                {
                    {"offset", page * 25}
                });
            if (isok)
            {
                BtnLoadMore.Visibility = json["hasMore"].ToObject<bool>() ? Visibility.Visible : Visibility.Collapsed;
                foreach (var token in json["data"])
                {
                    var artist = token["artists"].Select(t => NCArtist.CreateFromJson(t)).ToList();
                    ItemContainer.Children.Add(new SingleAlbum(NCAlbum.CreateFromJson(token), artist));
                }
            }
        }

        private void BtnLoadMore_OnClick(object sender, RoutedEventArgs e)
        {
            page++;
            RealLoad();
        }

        public void Dispose()
        {
            ItemContainer.Children.Clear();
        }
    }
}