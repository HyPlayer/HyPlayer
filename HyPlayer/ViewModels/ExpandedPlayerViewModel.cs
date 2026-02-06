using CommunityToolkit.Mvvm.ComponentModel;
using System;
using Windows.UI.Xaml.Media.Imaging;

namespace HyPlayer.ViewModels
{
    public partial class ExpandedPlayerViewModel : ObservableRecipient
    {
        [ObservableProperty]
        public partial string SongName { get; set; }
        [ObservableProperty]
        public partial string Album { get; set; }
        [ObservableProperty]
        public partial string Artist { get; set; }
        [ObservableProperty]
        public partial BitmapImage Cover { get; set; } = new BitmapImage(new Uri("ms-appx:///Assets/icon.png"));
    }
}
