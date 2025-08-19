using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Foundation.Collections;
using HyPlayer.ViewModels;
using AsyncAwaitBestPractices;
using HyPlayer.Classes;
using System.Diagnostics;
using HyPlayer.HyPlayControl;


namespace HyPlayer.Pages
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class HomePage : HomePageBase
    {
        public HomePage()
        {
            InitializeComponent();

        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);

            ViewModel.GetDataAsync().SafeFireAndForget();
        }

        private void Card_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            if (button == null) return;
            var playlist = button.CommandParameter as NCPlayList;
            Debug.WriteLine($"Card_Click: {playlist?.name}");
            Common.NavigatePage(typeof(SongListDetail), playlist, new Windows.UI.Xaml.Media.Animation.DrillInNavigationTransitionInfo());
        }

        private void SongCard_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            if (button == null) return;
            var song = button.CommandParameter as NCSong;
            Debug.WriteLine($"Card_Click: {song?.songname}");
            HyPlayList.AppendNcSong(song);
            
        }
    }

    public class HomePageBase : AppPageBase<HomeViewModel>
    {
        public HomePageBase()
        {
           
        }
    }
}
