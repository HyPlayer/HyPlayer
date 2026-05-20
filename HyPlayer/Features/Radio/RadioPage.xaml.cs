#region

using CommunityToolkit.Mvvm.DependencyInjection;
using CommunityToolkit.Mvvm.Messaging;
using HyPlayer.Domain;
using HyPlayer.Domain.Music;
using HyPlayer.Domain.Settings;
using HyPlayer.Features.User;
using HyPlayer.Infrastructure.Netease;
using HyPlayer.NeteaseApi;
using HyPlayer.NeteaseApi.ApiContracts;
using HyPlayer.NeteaseApi.ApiContracts.DjChannel;
using HyPlayer.Services.Abstractions;
using HyPlayer.Services.Cache;
using HyPlayer.Services.Downloads;
using HyPlayer.Services.Notifications.Messages;
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

namespace HyPlayer.Features.Radio;

public sealed partial class RadioPage : Page
{
    private readonly Setting _setting = Ioc.Default.GetRequiredService<Setting>();
    private readonly NeteaseCloudMusicApiHandler _api = Ioc.Default.GetRequiredService<NeteaseCloudMusicApiHandler>();
    private readonly INotificationService _notification = Ioc.Default.GetRequiredService<INotificationService>();
    private readonly INavigationService _navigation = Ioc.Default.GetRequiredService<INavigationService>();
    private readonly IAppNavigator _navigator = Ioc.Default.GetRequiredService<IAppNavigator>();

    private bool asc;
    private int i;
    private int page;
    private NCRadio Radio;
    private Task _programLoaderTask;
    private CancellationTokenSource _cancellationTokenSource = new CancellationTokenSource();
    private CancellationToken _cancellationToken;

    public ObservableCollection<NCSong> Songs = new();

    public RadioPage()
    {
        InitializeComponent();
        _cancellationToken = _cancellationTokenSource.Token;
    }

    protected override async void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        WeakReferenceMessenger.Default.UnregisterAll(this);

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

        _cancellationTokenSource?.Dispose();
    }

    private async Task LoadProgram()
    {
        _cancellationToken.ThrowIfCancellationRequested();
        var json = await SimpleCacher.GetOrCreateCacheAsync(CacheType.RadioPrograms, Radio.Id + "_" + page + asc,
                async () =>
                {
                    var rest = await _api.RequestAsync(NeteaseApis.DjChannelProgramsApi,
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
                        _notification.ShowMessage("贪婪加载冷却", $"渐进加载速度过于快, 将在 {cooldownTime * 10} 秒后尝试继续加载, 正在清洗请求");
                        return null;
                    }
                    else if (rest.IsError)
                    {
                        _notification.ShowMessage("加载电台节目错误", rest.Error?.Message ?? "未知错误");
                        return null;
                    }

                    return rest.Value;
                });


        NextPage.Visibility = json.Data?.More is true ? Visibility.Visible : Visibility.Collapsed;
        foreach (var jToken in json.Data?.Programs ?? [])
        {
            _cancellationToken.ThrowIfCancellationRequested();
            var song = jToken.MapToNCFmItem();
            song.Order = i++;
            song.TrackId = i;
            Songs.Add(song);
        }
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is string rid)
        {
            var json1 = await SimpleCacher.GetOrCreateCacheAsync(CacheType.RadioInfo, rid, async () =>
            {
                var json = await _api.RequestAsync(NeteaseApis.DjChannelDetailApi,
                    new DjChannelDetailRequest() { Id = rid }, _cancellationToken);
                if (json.IsError)
                {
                    _notification.ShowMessage("获取电台信息失败", json.Error?.Message ?? "未知错误");
                    return null;
                }

                return json.Value;
            });

            Radio = json1.RadioData.MapToNCRadio();
        }

        if (e.Parameter is NCRadio radio) Radio = radio;

        TextBoxRadioName.Text = Radio.Name;
        TextBoxDJ.Content = Radio.DJ.Name;
        TextBlockDesc.Text = Radio.Description;
        if (_setting.noImage)
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
        SongContainer.QueueScope = SongListQueueScope.Radio(Radio.Id);
        _programLoaderTask = LoadProgram();
        if (_setting.greedlyLoadPlayContainerItems)
            WeakReferenceMessenger.Default.Register<GlobalSecondTimerMessage>(this, (r, _) => ((RadioPage)r).GreedlyLoad());
    }

    int treashold = 3;
    int cooldownTime = 0;

    private void GreedlyLoad()
    {
        _ = _notification.InvokeOnUIThread(() =>
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
                WeakReferenceMessenger.Default.Unregister<GlobalSecondTimerMessage>(this);
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
        var playlist = Ioc.Default.GetRequiredService<IPlaylistService>();
        await _navigator.AppendAsync(new MusicResource.Radio(Radio.Id));
        if (asc) playlist.ReverseList();
        await playlist.MoveToAsync(playlist.Items.FirstOrDefault());
    }

    private void TextBoxDJ_OnTapped(object sender, RoutedEventArgs routedEventArgs)
    {
        _navigation.Navigate(typeof(Me), Radio.DJ.Id);
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
        var playlist = Ioc.Default.GetRequiredService<IPlaylistService>();
        await playlist.AppendRadioListAsync(Radio.Id, asc);
    }

    private async void ButtonDownloadAll_OnClick(object sender, RoutedEventArgs e)
    {
        var result = new List<NCSong>();
        bool? hasMore = true;
        var page = 0;
        while (hasMore is true)
        {
            var json = await SimpleCacher.GetOrCreateCacheAsync(CacheType.RadioPrograms, Radio.Id + "_" + page + asc,
                        async () =>
                        {
                            var rest = await _api.RequestAsync(NeteaseApis.DjChannelProgramsApi,
                                new DjChannelProgramsRequest()
                                {
                                    RadioId = Radio.Id,
                                    Limit = 100,
                                    Offset = page * 100,
                                    Asc = asc
                                }, _cancellationToken);
                            if (rest.IsError)
                            {
                                _notification.ShowMessage("加载电台节目错误", rest.Error?.Message ?? "未知错误");
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

        DownloadManager.AddDownload(result);
    }
}
