#region

using CommunityToolkit.Mvvm.DependencyInjection;
using HyPlayer.Domain.Music;
using HyPlayer.Infrastructure.Netease;
using HyPlayer.NeteaseApi;
using HyPlayer.NeteaseApi.ApiContracts;
using HyPlayer.NeteaseApi.ApiContracts.Video;
using HyPlayer.Services.Abstractions;
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Windows.Media.Core;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;

#endregion

// https://go.microsoft.com/fwlink/?LinkId=234238 上介绍了“空白页”项模板

namespace HyPlayer.Features.Video;

/// <summary>
///     可用于自身或导航至 Frame 内部的空白页。
/// </summary>
public sealed partial class MVPage : Page
{
    private readonly NeteaseCloudMusicApiHandler _api = Ioc.Default.GetRequiredService<NeteaseCloudMusicApiHandler>();
        private readonly ITeachingTipService _teachingTipService = Ioc.Default.GetRequiredService<ITeachingTipService>();

    private readonly List<NCMlog> sources = new();
    private string MVId;
    private string mvquality = "1080";
    private string songid;
    private CancellationTokenSource _cancellationTokenSource = new CancellationTokenSource();
    private CancellationToken _cancellationToken;
    private Task _relateiveLoaderTask;
    private Task _videoLoaderTask;
    private Task _videoInfoLoaderTask;

    public MVPage()
    {
        InitializeComponent();
        _cancellationToken = _cancellationTokenSource.Token;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is NCSong input)
        {
            MVId = input.MVId.ToString();
            songid = input.SongId;
            _relateiveLoaderTask = LoadRelateive();
        }
        else
        {
            MVId = e.Parameter.ToString();
            LoadThings();
        }
    }

    private void LoadThings()
    {
        Ioc.Default.GetRequiredService<IPlaybackControlService>().Pause();
        _videoLoaderTask = LoadVideo();
        _videoInfoLoaderTask = LoadVideoInfo();
    }

    private async Task LoadRelateive()
    {
        _cancellationToken.ThrowIfCancellationRequested();
        var json = await _api.RequestAsync(NeteaseApis.MlogRcmdFeedListApi,
                new MlogRcmdFeedListRequest()
                {
                    Id = MVId,
                    SongId = songid,
                    Limit = 10
                });
        if (json.IsError)
        {
            _teachingTipService.Items.Enqueue(new("加载相关视频时出错", json.Error.Message));
            return;
        }

        foreach (var jToken in json.Value.Data?.Feeds ?? [])
            sources.Add(jToken.Resource?.BaseData.MapToNcMlog());

        RelativeList.ItemsSource = sources;

        RelativeList.SelectedIndex = 0;
    }

    protected override async void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        MediaPlayerElement.MediaPlayer?.Pause();
        if (_relateiveLoaderTask != null && !_relateiveLoaderTask.IsCompleted)
        {
            try
            {
                _cancellationTokenSource.Cancel();
                await _relateiveLoaderTask;
            }
            catch
            {
            }
        }

        if (_videoLoaderTask != null && !_videoLoaderTask.IsCompleted)
        {
            try
            {
                _cancellationTokenSource.Cancel();
                await _videoLoaderTask;
            }
            catch
            {
            }
        }

        if (_videoInfoLoaderTask != null && !_videoInfoLoaderTask.IsCompleted)
        {
            try
            {
                _cancellationTokenSource.Cancel();
                await _videoInfoLoaderTask;
            }
            catch
            {
            }
        }

        MediaPlayerElement.Source = null;
        _cancellationTokenSource?.Dispose();
    }

    private async Task LoadVideo()
    {

        //纯MV
        _cancellationToken.ThrowIfCancellationRequested();
        LoadingControl.IsLoading = true;
        string url;
        if (VideoRegex().IsMatch(MVId))
        {
            var json = await _api.RequestAsync(NeteaseApis.VideoUrlApi,
                new VideoUrlRequest()
                {
                    Id = MVId,
                    Resolution = mvquality
                }, _cancellationToken);
            if (json.IsError)
            {
                _teachingTipService.Items.Enqueue(new("加载视频时出错", json.Error.Message));
                return;
            }

            url = json.Value.Data?.Url;
        }
        else
        {
            var json = await _api.RequestAsync(NeteaseApis.MlogUrlApi,
                new MlogUrlRequest()
                {
                    Id = MVId,
                    Resolution = mvquality
                }, _cancellationToken);
            if (json.IsError)
            {
                _teachingTipService.Items.Enqueue(new("加载视频时出错", json.Error.Message));
                return;
            }

            url = json.Value.Data?.GetValueOrDefault(MVId).UrlInfo?.Url;
        }

        MediaPlayerElement.Source = MediaSource.CreateFromUri(new Uri(url!));
        var mediaPlayer = MediaPlayerElement.MediaPlayer;
        mediaPlayer.Play();
        LoadingControl.IsLoading = false;
    }

    private async Task LoadVideoInfo()
    {
        _cancellationToken.ThrowIfCancellationRequested();
        if (MvIdRegex().IsMatch(MVId))
        {
            var json = await _api.RequestAsync(NeteaseApis.VideoDetailApi,
                   new VideoDetailRequest()
                   {
                       Id = MVId
                   }, _cancellationToken);
            if (json.IsError)
            {
                _teachingTipService.Items.Enqueue(new("加载视频信息时出错", json.Error.Message));
                return;
            }

            TextBoxVideoName.Text = json.Value?.Data?.Resource?.Data?.Name;
            TextBoxSinger.Text = string.Join(" / ", json.Value?.Data?.Resource?.Data?.ArtistName);
            TextBoxDesc.Text = json.Value?.Data?.Resource?.Data?.Description;
            TextBoxOtherInfo.Text =
                $"发布时间: {json.Value?.Data?.Resource?.Data?.PublishTime} | 播放量: {json.Value?.Data?.Resource?.Data?.PlayCount}次 | 收藏量: {json.Value?.Data?.Resource?.Data?.SubCount}次";
            foreach (var br in json.Value?.Data?.Resource?.Data?.Brs ?? [])
            {
                VideoQualityBox.Items?.Add(br.Br.ToString());
            }

            VideoQualityBox.SelectedItem = json.Value?.Data?.Resource?.Mp?.PlayResolution.ToString();
        }
        else
        {
            var json = await _api.RequestAsync(NeteaseApis.MlogDetailApi,
                    new MlogDetailRequest()
                    {
                        MlogId = MVId
                    }, _cancellationToken);
            if (json.IsError)
            {
                _teachingTipService.Items.Enqueue(new("加载视频信息时出错", json.Error.Message));
                return;
            }

            TextBoxVideoName.Text = json.Value?.Data?.Resource?.Content?.Title;

            TextBoxSinger.Text = json.Value?.Data?.Resource?.Profile?.Nickname;
            TextBoxDesc.Text = json.Value?.Data?.Resource?.Content?.Text;
            TextBoxOtherInfo.Text =
                $"发布时间: {json.Value?.Data?.Resource?.PublishTime} | 播放量: {json.Value?.Data?.Resource?.LikedCount}次";
        }
    }

    private void VideoQualityBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        mvquality = VideoQualityBox.SelectedItem?.ToString();
        _videoLoaderTask = LoadVideo();
    }

    private void RelativeList_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        MVId = (RelativeList.SelectedItem is NCMlog ? (NCMlog)RelativeList.SelectedItem : default).Id;
        LoadThings();
    }

    [GeneratedRegex("^[0-9]*$")]
    private static partial Regex MvIdRegex();
    [GeneratedRegex("^[0-9]*$")]
    private static partial Regex VideoRegex();
}
