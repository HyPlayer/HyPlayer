#region

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media.Imaging;
using Windows.UI.Xaml.Navigation;
using HyPlayer.Classes;
using HyPlayer.HyPlayControl;
using NeteaseCloudMusicApi;

#endregion

namespace HyPlayer.Pages;

public sealed partial class RadioPage : Page, IDisposable
{
    private bool asc;
    private int i;
    private int page;
    private NCRadio Radio;

    public ObservableCollection<NCSong> Songs = new();

    public RadioPage()
    {
        InitializeComponent();
    }

    public void Dispose()
    {
        ImageRect.ImageSource = null;
        Songs.Clear();
    }

    private async Task LoadProgram()
    {
        try
        {
            var json = await Common.ncapi.RequestAsync(CloudMusicApiProviders.DjProgram,
                new Dictionary<string, object>
                {
                    { "rid", Radio.id },
                    { "offset", page * 30 },
                    { "asc", asc }
                });
            NextPage.Visibility = json["more"].ToObject<bool>() ? Visibility.Visible : Visibility.Collapsed;
            foreach (var jToken in json["programs"])
            {
                var song = NCFmItem.CreateFromJson(jToken);
                song.Type = HyPlayItemType.Radio;
                song.Order = i++;
                Songs.Add(song);
            }
        }
        catch (Exception ex)
        {
            Common.AddToTeachingTipLists(ex.Message, (ex.InnerException ?? new Exception()).Message);
        }
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is string rid)
            try
            {
                var json1 = await Common.ncapi.RequestAsync(CloudMusicApiProviders.DjDetail,
                    new Dictionary<string, object> { { "rid", rid } });
                Radio = NCRadio.CreateFromJson(json1["djRadio"]);
            }
            catch (Exception ex)
            {
                Common.AddToTeachingTipLists(ex.Message, (ex.InnerException ?? new Exception()).Message);
            }

        if (e.Parameter is NCRadio radio) Radio = radio;

        TextBoxRadioName.Text = Radio.name;
        TextBoxDJ.Content = Radio.DJ.name;
        TextBlockDesc.Text = Radio.desc;
        ImageRect.ImageSource =
            Common.Setting.noImage
                ? null
                : new BitmapImage(new Uri(Radio.cover + "?param=" + StaticSource.PICSIZE_SONGLIST_DETAIL_COVER));
        Songs.Clear();
        SongContainer.ListSource = "rd" + Radio.id;
        LoadProgram();
    }

    private void NextPage_OnClickPage_OnClick(object sender, RoutedEventArgs e)
    {
        page++;
        LoadProgram();
    }

    private async void ButtonPlayAll_OnClick(object sender, RoutedEventArgs e)
    {
        try
        {
            await HyPlayList.AppendNcSource("rd" + Radio.id);
            if (asc) HyPlayList.List.Reverse();
            HyPlayList.SongAppendDone();
            HyPlayList.SongMoveTo(0);
        }
        catch (Exception ex)
        {
            Common.AddToTeachingTipLists(ex.Message, (ex.InnerException ?? new Exception()).Message);
        }
    }

    private void TextBoxDJ_OnTapped(object sender, RoutedEventArgs routedEventArgs)
    {
        Common.NavigatePage(typeof(Me), Radio.DJ.id);
    }

    private void Button_Click(object sender, RoutedEventArgs e)
    {
        Songs.Clear();
        page = 0;
        i = 0;
        asc = !asc;
        LoadProgram();
    }

    private async void BtnAddAll_Clicked(object sender, RoutedEventArgs e)
    {
        await HyPlayList.AppendRadioList(Radio.id);
        HyPlayList.SongAppendDone();
    }
}