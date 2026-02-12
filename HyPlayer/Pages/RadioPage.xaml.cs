#region

using HyPlayer.Classes;
using HyPlayer.HyPlayControl;
using HyPlayer.NeteaseApi.ApiContracts;
using HyPlayer.NeteaseApi.ApiContracts.DjChannel;
using ObservableCollections;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media.Imaging;
using Windows.UI.Xaml.Navigation;

#endregion

namespace HyPlayer.Pages;

public sealed partial class RadioPage : Page
{
    private bool asc;
    private int i;
    private int page;
    private NCRadio Radio;
    private Task _programLoaderTask;
    private CancellationTokenSource _cancellationTokenSource = new CancellationTokenSource();
    private CancellationToken _cancellationToken;

    public ObservableList<NCSong> Songs = new();

    public RadioPage()
    {
        InitializeComponent();
        _cancellationToken = _cancellationTokenSource.Token;
    }

    protected override async void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        if (_programLoaderTask != null && !_programLoaderTask.IsCompleted)
        {
            try
            {
                _cancellationTokenSource.Cancel();
                await _programLoaderTask;
            }
            catch
            {
                //Ignore
            }
        }

        _cancellationTokenSource.Dispose();
    }

    private async Task LoadProgram()
    {
        _cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var json = await SimpleCacher.GetOrCreateCacheAsync(CacheType.RadioPrograms, Radio.Id + "_" + page + asc,
                async () =>
                {
                    var rest = await Common.NeteaseAPI!.RequestAsync(NeteaseApis.DjChannelProgramsApi,
                        new DjChannelProgramsRequest()
                        {
                            RadioId = Radio.Id,
                            Limit = 100,
                            Offset = page * 100,
                            Asc = asc
                        }, _cancellationToken);
                    if (rest.IsError && rest.Error?.ErrorCode == 405)
                    {
                        treashold = ++cooldownTime * 10;
                        page--;
                        Common.AddToTeachingTipLists("贪婪加载冷却", $"渐进加载速度过于快, 将在 {cooldownTime * 10} 秒后尝试继续加载, 正在清洗请求");
                        return null;
                    }
                    else if (rest.IsError)
                    {
                        Common.AddToTeachingTipLists("加载电台节目错误", rest.Error?.Message ?? "未知错误");
                        return null;
                    }

                    return rest.Value;
                });


            NextPage.Visibility = json.Data?.More is true ? Visibility.Visible : Visibility.Collapsed;
            var list = new List<NCFmItem>(json.Data?.Programs?.Length ?? 0);
            foreach (var jToken in json.Data?.Programs ?? [])
            {
                _cancellationToken.ThrowIfCancellationRequested();
                var song = jToken.MapToNCFmItem();
                song.Order = i++;
                song.TrackId = i;
                list.Add(song);
            }
            Songs.AddRange(list);
        }
        catch (Exception ex)
        {
            if (ex.GetType() != typeof(TaskCanceledException) && ex.GetType() != typeof(OperationCanceledException))
                Common.AddToTeachingTipLists(ex.Message, (ex.InnerException ?? new Exception()).Message);
        }
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is string rid)
            try
            {
                var json1 = await SimpleCacher.GetOrCreateCacheAsync(CacheType.RadioInfo, rid, async () =>
                {
                    var json = await Common.NeteaseAPI!.RequestAsync(NeteaseApis.DjChannelDetailApi,
                        new DjChannelDetailRequest() { Id = rid }, _cancellationToken);
                    if (json.IsError)
                    {
                        Common.AddToTeachingTipLists("获取电台信息失败", json.Error?.Message ?? "未知错误");
                        return null;
                    }

                    return json.Value;
                });

                Radio = json1.RadioData.MapToNCRadio();
            }
            catch (Exception ex)
            {
                Common.AddToTeachingTipLists(ex.Message, (ex.InnerException ?? new Exception()).Message);
            }

        if (e.Parameter is NCRadio radio) Radio = radio;

        TextBoxRadioName.Text = Radio.Name;
        TextBoxDJ.Content = Radio.DJ.Name;
        TextBlockDesc.Text = Radio.Description;
        if (Common.Setting.noImage)
        {
            ImageRect.ImageSource = null;
        }
        else
        {
            var img = new BitmapImage();
            ImageRect.ImageSource = img;
            img.UriSource = new Uri(Radio.Cover + "?param=" + StaticSource.PICSIZE_SONGLIST_DETAIL_COVER);
        }

        Songs.Clear();
        SongContainer.ListSource = "rd" + Radio.Id;
        _programLoaderTask = LoadProgram();
        if (Common.Setting.greedlyLoadPlayContainerItems)
            HyPlayList.OnTimerTicked += GreedlyLoad;
    }

    int treashold = 3;
    int cooldownTime = 0;

    private void GreedlyLoad()
    {
        _ = Common.Invoke(() =>
        {
            if (treashold > 10)
            {
                treashold--;
                return;
            }

            if (Songs.Count > 0 && NextPage.Visibility == Visibility.Visible && treashold-- <= 0)
            {
                NextPage_OnClickPage_OnClick(null, null);
                treashold = 3;
            }
            else if (SongContainer.Songs.Count > 0 && NextPage.Visibility == Visibility.Collapsed)
            {
                HyPlayList.OnTimerTicked -= GreedlyLoad;
            }
        });
    }

    private void NextPage_OnClickPage_OnClick(object sender, RoutedEventArgs e)
    {
        page++;
        _programLoaderTask = LoadProgram();
    }

    private async void ButtonPlayAll_OnClick(object sender, RoutedEventArgs e)
    {
        try
        {
            await HyPlayList.AppendNcSource("rd" + Radio.Id);
            if (asc) HyPlayList.List.Reverse();
            HyPlayList.SongMoveTo(HyPlayList.List.FirstOrDefault());
        }
        catch (Exception ex)
        {
            Common.AddToTeachingTipLists(ex.Message, (ex.InnerException ?? new Exception()).Message);
        }
    }

    private void TextBoxDJ_OnTapped(object sender, RoutedEventArgs routedEventArgs)
    {
        Common.NavigatePage(typeof(Me), Radio.DJ.Id);
    }

    private void Button_Click(object sender, RoutedEventArgs e)
    {
        Songs.Clear();
        page = 0;
        i = 0;
        asc = !asc;
        _programLoaderTask = LoadProgram();
    }

    private async void BtnAddAll_Clicked(object sender, RoutedEventArgs e)
    {
        await HyPlayList.AppendRadioList(Radio.Id, asc);
    }

    private async void ButtonDownloadAll_OnClick(object sender, RoutedEventArgs e)
    {
        var result = new List<NCSong>();
        try
        {
            bool? hasMore = true;
            var page = 0;
            while (hasMore is true)
                try
                {
                    var json = await SimpleCacher.GetOrCreateCacheAsync(CacheType.RadioPrograms, Radio.Id + "_" + page + asc,
                        async () =>
                        {
                            var rest = await Common.NeteaseAPI!.RequestAsync(NeteaseApis.DjChannelProgramsApi,
                                new DjChannelProgramsRequest()
                                {
                                    RadioId = Radio.Id,
                                    Limit = 100,
                                    Offset = page * 100,
                                    Asc = asc
                                }, _cancellationToken);
                            if (rest.IsError)
                            {
                                Common.AddToTeachingTipLists("加载电台节目错误", rest.Error?.Message ?? "未知错误");
                                return null;
                            }

                            return rest.Value;
                        });
                    hasMore = json?.Data?.More is true;
                    foreach (var jToken in json?.Data?.Programs ?? [])
                    {
                        _cancellationToken.ThrowIfCancellationRequested();
                        var song = jToken.MapToNCFmItem();
                        song.Order = i++;
                        song.TrackId = i;
                        result.Add(song);
                    }

                    page++;
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