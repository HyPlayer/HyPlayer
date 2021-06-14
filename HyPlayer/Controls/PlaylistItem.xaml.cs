using HyPlayer.Classes;
using HyPlayer.Pages;
using System;
using System.Threading.Tasks;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media.Animation;
using Windows.UI.Xaml.Media.Imaging;

//https://go.microsoft.com/fwlink/?LinkId=234236 上介绍了“用户控件”项模板

namespace HyPlayer.Controls
{
    public sealed partial class PlaylistItem : UserControl
    {
        private NCPlayList playList;
        public PlaylistItem(NCPlayList playList)
        {
            InitializeComponent();
            this.playList = playList;
            Task.Run(() =>
            {
                Common.Invoke(() =>
                {
                    ImageContainer.Source = new BitmapImage(new Uri(playList.cover + "?param=" + StaticSource.PICSIZE_PLAYLIST_ITEM_COVER));
                    TextBlockPLName.Text = playList.name;
                    TextBlockPLAuthor.Text = playList.creater.name;
                });
            });


        }

        private void UIElement_OnTapped(object sender, TappedRoutedEventArgs e)
        {
            ConnectedAnimationService.GetForCurrentView().PrepareToAnimate("SongListExpand", ImageContainer);
            ConnectedAnimationService.GetForCurrentView().PrepareToAnimate("SongListExpandAcrylic", GridInfo);
            Common.BaseFrame.Navigate(typeof(SongListDetail), playList, new DrillInNavigationTransitionInfo());
        }

        private void UIElement_OnPointerEntered(object sender, PointerRoutedEventArgs e)
        {
            if (Common.Setting.expandAnimation)
                StoryboardOut.Begin();
        }

        private void UIElement_OnPointerExited(object sender, PointerRoutedEventArgs e)
        {
            if (Common.Setting.expandAnimation)
                StoryboardIn.Begin();
        }
    }
}
