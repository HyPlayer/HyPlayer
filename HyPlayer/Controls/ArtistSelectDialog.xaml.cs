using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;
using HyPlayer.Classes;
using HyPlayer.Pages;

// https://go.microsoft.com/fwlink/?LinkId=234238 上介绍了“内容对话框”项模板

namespace HyPlayer.Controls
{
    public sealed partial class ArtistSelectDialog : ContentDialog
    {
        private List<NCArtist> aartists;
        public ArtistSelectDialog(List<NCArtist> artists)
        {
            aartists = artists;
            this.InitializeComponent();
            ListViewArtists.Items?.Clear();
            artists.ForEach(t=> ListViewArtists.Items?.Add(t.name));
        }


        private void ListViewArtists_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            Common.BaseFrame.Navigate(typeof(ArtistPage), aartists[ListViewArtists.SelectedIndex].id);
            this.Hide();
        }
    }
}
