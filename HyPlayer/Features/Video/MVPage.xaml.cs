#region

using CommunityToolkit.Mvvm.DependencyInjection;
using HyPlayer.Domain.Comments;
using HyPlayer.Domain.Music;
using HyPlayer.Infrastructure.Netease;
using HyPlayer.PlayCore.Abstraction.Interfaces.Provider;
using HyPlayer.PlayCore.Abstraction.Models;
using HyPlayer.PlayCore.Abstraction.Models.Containers;
using HyPlayer.PlayCore.Abstraction.Models.Resources;
using HyPlayer.NeteaseProvider.Models;
using HyPlayer.PlayCore.Abstraction.Models.SingleItems;
using HyPlayer.Services.Abstractions;
using HyPlayer.UI.Lists;
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
    private readonly IRichMediaProvidable _richMediaProvider = Ioc.Default.GetRequiredService<IRichMediaProvidable>();
    private readonly INotificationService _notification = Ioc.Default.GetRequiredService<INotificationService>();

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
        if (e.Parameter is SongListItemViewModel row)
        {
            MVId = row.MVId;
            songid = row.SongId;
            _relateiveLoaderTask = LoadRelateive();
        }
        else if (e.Parameter is NeteaseSong song)
        {
            MVId = song.MvId ?? string.Empty;
            songid = song.ActualId;
            _relateiveLoaderTask = LoadRelateive();
        }
        else if (e.Parameter is SingleSongBase providerSong)
        {
            MVId = e.Parameter.ToString();
            songid = providerSong.ActualId;
            LoadThings();
        }
        else if (e.Parameter is NCSong input)
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
        LoadComment();
    }

    private async Task LoadRelateive()
    {
        _cancellationToken.ThrowIfCancellationRequested();
        var result = await _richMediaProvider.GetRichMediaFeedAsync($"song:{songid}", 0, 10, _cancellationToken);
        foreach (var item in result.Items)
        {
            sources.Add(MapRichMediaToNcMlog(item));
        }

        RelativeList.ItemsSource = sources;

        RelativeList.SelectedIndex = 0;
    }

    private void LoadComment()
    {
        if (Regex.IsMatch(MVId, "^[0-9]*$"))
            CommentFrame.Navigate(typeof(Comments.Comments), CommentTarget.MV(MVId));
        else
            CommentFrame.Navigate(typeof(Comments.Comments), CommentTarget.MLog(MVId));
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
        var resource = await _richMediaProvider.GetRichMediaResourceAsync(
            MVId,
            MvIdRegex().IsMatch(MVId) ? NeteaseTypeIds.Mv : NeteaseTypeIds.MBlog,
            mvquality,
            _cancellationToken);
        var resourceResult = resource is null ? null : await resource.GetResourceAsync(ctk: _cancellationToken);
        if (resourceResult is not IResourceResultOf<Uri?> uriResource)
        {
            _notification.ShowMessage("加载视频时出错", "视频资源为空");
            return;
        }

        var uri = await uriResource.GetResourceAsync(_cancellationToken);
        if (uri is null)
        {
            _notification.ShowMessage("加载视频时出错", "视频地址为空");
            return;
        }

        MediaPlayerElement.Source = MediaSource.CreateFromUri(uri);
        var mediaPlayer = MediaPlayerElement.MediaPlayer;
        mediaPlayer.Play();
        LoadingControl.IsLoading = false;
    }

    private async Task LoadVideoInfo()
    {
        _cancellationToken.ThrowIfCancellationRequested();
        if (MvIdRegex().IsMatch(MVId))
        {
            var richMedia = await _richMediaProvider.GetRichMediaAsync(MVId, NeteaseTypeIds.Mv, _cancellationToken);
            if (richMedia is not NeteaseMv mv)
            {
                _notification.ShowMessage("加载视频信息时出错", "视频信息为空");
                return;
            }

            TextBoxVideoName.Text = mv.Name;
            TextBoxSinger.Text = mv.CreatorName;
            TextBoxDesc.Text = mv.Description;
            TextBoxOtherInfo.Text =
                $"发布时间: {mv.PublishTime} | 播放量: {mv.PlayCount}次 | 收藏量: {mv.SubCount}次";
            foreach (var br in mv.AvailableQualities)
            {
                VideoQualityBox.Items?.Add(br.ToString());
            }

            VideoQualityBox.SelectedItem = mv.AvailableQualities.Count > 0 ? mv.AvailableQualities[0].ToString() : mvquality;
        }
        else
        {
            var richMedia = await _richMediaProvider.GetRichMediaAsync(MVId, NeteaseTypeIds.MBlog, _cancellationToken);
            if (richMedia is not NeteaseMlog mlog)
            {
                _notification.ShowMessage("加载视频信息时出错", "视频信息为空");
                return;
            }

            TextBoxVideoName.Text = mlog.Name;

            TextBoxSinger.Text = mlog.CreatorName;
            TextBoxDesc.Text = mlog.Description;
            TextBoxOtherInfo.Text =
                $"发布时间: {mlog.PublishTime} | 播放量: {mlog.LikedCount}次";
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

    private static NCMlog MapRichMediaToNcMlog(RichMediaBase item)
    {
        return new NCMlog
        {
            Id = item.ActualId ?? string.Empty,
            Title = item.Name,
            Description = item.Description,
            Duration = (int)item.Duration,
            Cover = item is NeteaseMlog mlog ? mlog.CoverUrl : string.Empty
        };
    }
}
