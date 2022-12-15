#region

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
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
    public bool IsDisposed = false;

    public ObservableCollection<NCSong> Songs = new();

    public RadioPage()
    {
        InitializeComponent();
    }

    public void Dispose()
    {
        if (IsDisposed) return;
        ImageRect.ImageSource = null;
        SongContainer.Dispose();
        Songs.Clear();
        IsDisposed = true;
        GC.SuppressFinalize(this);
        GC.Collect();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        Dispose();
    }

    private async Task LoadProgram()
    {
        if (IsDisposed) throw new ObjectDisposedException(nameof(RadioPage));
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
        _ = LoadProgram();
    }

    private void NextPage_OnClickPage_OnClick(object sender, RoutedEventArgs e)
    {
        if (IsDisposed) throw new ObjectDisposedException(nameof(RadioPage));
        page++;
        _ = LoadProgram();
    }

    private async void ButtonPlayAll_OnClick(object sender, RoutedEventArgs e)
    {
        if (IsDisposed) throw new ObjectDisposedException(nameof(RadioPage));
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
        if (IsDisposed) throw new ObjectDisposedException(nameof(RadioPage));
        Common.NavigatePage(typeof(Me), Radio.DJ.id);
    }

    private void Button_Click(object sender, RoutedEventArgs e)
    {
        if (IsDisposed) throw new ObjectDisposedException(nameof(RadioPage));
        Songs.Clear();
        page = 0;
        i = 0;
        asc = !asc;
        _ = LoadProgram();
    }

    private async void BtnAddAll_Clicked(object sender, RoutedEventArgs e)
    {
        if (IsDisposed) throw new ObjectDisposedException(nameof(RadioPage));
        await HyPlayList.AppendRadioList(Radio.id, asc);
        HyPlayList.SongAppendDone();
    }

    private async void ButtonDownloadAll_OnClick(object sender, RoutedEventArgs e)
    {
        if (IsDisposed) throw new ObjectDisposedException(nameof(RadioPage));
        var result = new List<NCSong>();
        try
        {
            bool? hasMore = true;
            var page = 0;
            while (hasMore is true)
                try
                {
                    var json = await Common.ncapi.RequestAsync(CloudMusicApiProviders.DjProgram,
                        new Dictionary<string, object>
                        {
                            { "rid", Radio.id },
                            { "offset", page++ * 100 },
                            { "limit", 100 },
                            { "asc", asc }
                        });
                    hasMore = json["more"]?.ToObject<bool>();
                    if (json["programs"] is not null)
                        result.AddRange(json["programs"].Select(t => (NCSong)NCFmItem.CreateFromJson(t)).ToList());
                }
                catch (Exception ex)
                {
                    Common.AddToTeachingTipLists(ex.Message,
                        (ex.InnerException ?? new Exception()).Message);
                }
        }
        catch (Exception ex)
        {
            Common.AddToTeachingTipLists(ex.Message, (ex.InnerException ?? new Exception()).Message);
        }
        DownloadManager.AddDownload(result);
    }
}