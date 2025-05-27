#region

using HyPlayer.Classes;
using HyPlayer.HyPlayControl;
using HyPlayer.NeteaseApi.ApiContracts;
using HyPlayer.NeteaseApi.ApiContracts.Video;
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

namespace HyPlayer.Pages;

/// <summary>
///     可用于自身或导航至 Frame 内部的空白页。
/// </summary>
public sealed partial class MVPage : Page, IDisposable
{
    private readonly List<NCMlog> sources = new();
    private string mvid;
    private string mvquality = "1080";
    private string songid;
    private bool disposedValue = false;
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
            mvid = input.mvid.ToString();
            songid = input.sid;
            _relateiveLoaderTask = LoadRelateive();
        }
        else
        {
            mvid = e.Parameter.ToString();
            LoadThings();
        }
    }

    private void LoadThings()
    {
        HyPlayList.Player.PauseAll();
        _videoLoaderTask = LoadVideo();
        _videoInfoLoaderTask = LoadVideoInfo();
        LoadComment();
    }

    private async Task LoadRelateive()
    {
        if (disposedValue) throw new ObjectDisposedException(nameof(MVPage));
        _cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var json = await Common.NeteaseAPI.RequestAsync(NeteaseApis.MlogRcmdFeedListApi,
                new MlogRcmdFeedListRequest()
                {
                    Id = mvid,
                    SongId = songid,
                    Limit = 10
                });
            if (json.IsError)
            {
                Common.AddToTeachingTipLists("加载相关视频时出错", json.Error.Message);
                return;
            }

            foreach (var jToken in json.Value.Data?.Feeds ?? [])
                sources.Add(jToken.Resource?.BaseData.MapToNcMlog());

            RelativeList.ItemsSource = sources;
        }
        catch (Exception ex)
        {
            Common.AddToTeachingTipLists(ex.Message, (ex.InnerException ?? new Exception()).Message);
        }

        RelativeList.SelectedIndex = 0;
    }

    private void LoadComment()
    {
        if (disposedValue) throw new ObjectDisposedException(nameof(MVPage));
        if (Regex.IsMatch(mvid, "^[0-9]*$"))
            CommentFrame.Navigate(typeof(Comments), "mv" + mvid);
        else
            CommentFrame.Navigate(typeof(Comments), "mb" + mvid);
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

        Dispose();
    }

    private async Task LoadVideo()
    {
        if (disposedValue) throw new ObjectDisposedException(nameof(MVPage));

        //纯MV
        try
        {
            _cancellationToken.ThrowIfCancellationRequested();
            LoadingControl.IsLoading = true;
            string url;
            if (Regex.IsMatch(mvid, "^[0-9]*$"))
            {
                var json = await Common.NeteaseAPI.RequestAsync(NeteaseApis.VideoUrlApi,
                    new VideoUrlRequest()
                    {
                        Id = mvid,
                        Resolution = mvquality
                    }, _cancellationToken);
                if (json.IsError)
                {
                    Common.AddToTeachingTipLists("加载视频时出错", json.Error.Message);
                    return;
                }

                url = json.Value.Data?.Url;
            }
            else
            {
                var json = await Common.NeteaseAPI.RequestAsync(NeteaseApis.MlogUrlApi,
                    new MlogUrlRequest()
                    {
                        Id = mvid,
                        Resolution = mvquality
                    }, _cancellationToken);
                if (json.IsError)
                {
                    Common.AddToTeachingTipLists("加载视频时出错", json.Error.Message);
                    return;
                }

                url = json.Value.Data?.GetValueOrDefault(mvid).UrlInfo?.Url;
            }

            MediaPlayerElement.Source = MediaSource.CreateFromUri(new Uri(url!));
            var mediaPlayer = MediaPlayerElement.MediaPlayer;
            mediaPlayer.Play();
            LoadingControl.IsLoading = false;
        }
        catch (Exception ex)
        {
            Common.AddToTeachingTipLists(ex.Message, (ex.InnerException ?? new Exception()).Message);
        }
    }

    private async Task LoadVideoInfo()
    {
        if (disposedValue) throw new ObjectDisposedException(nameof(MVPage));
        _cancellationToken.ThrowIfCancellationRequested();
        if (Regex.IsMatch(mvid, "^[0-9]*$"))
        {
            try
            {
                var json = await Common.NeteaseAPI.RequestAsync(NeteaseApis.VideoDetailApi,
                    new VideoDetailRequest()
                    {
                        Id = mvid
                    }, _cancellationToken);
                if (json.IsError)
                {
                    Common.AddToTeachingTipLists("加载视频信息时出错", json.Error.Message);
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
            catch (Exception ex)
            {
                Common.AddToTeachingTipLists(ex.Message, (ex.InnerException ?? new Exception()).Message);
            }
        }
        else
        {
            try
            {
                var json = await Common.NeteaseAPI.RequestAsync(NeteaseApis.MlogDetailApi,
                    new MlogDetailRequest()
                    {
                        MlogId = mvid
                    }, _cancellationToken);
                if (json.IsError)
                {
                    Common.AddToTeachingTipLists("加载视频信息时出错", json.Error.Message);
                    return;
                }

                TextBoxVideoName.Text = json.Value?.Data?.Resource?.Content?.Title;

                TextBoxSinger.Text = json.Value?.Data?.Resource?.Profile?.Nickname;
                TextBoxDesc.Text = json.Value?.Data?.Resource?.Content?.Text;
                TextBoxOtherInfo.Text =
                    $"发布时间: {json.Value?.Data?.Resource?.PublishTime} | 播放量: {json.Value?.Data?.Resource?.LikedCount}次";
            }
            catch (Exception ex)
            {
                Common.AddToTeachingTipLists(ex.Message, (ex.InnerException ?? new Exception()).Message);
            }
        }
    }

    private void VideoQualityBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (disposedValue) throw new ObjectDisposedException(nameof(MVPage));
        mvquality = VideoQualityBox.SelectedItem?.ToString();
        _videoLoaderTask = LoadVideo();
    }

    private void RelativeList_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (disposedValue) throw new ObjectDisposedException(nameof(MVPage));
        mvid = (RelativeList.SelectedItem is NCMlog ? (NCMlog)RelativeList.SelectedItem : default).id;
        LoadThings();
    }

    private void Dispose(bool disposing)
    {
        if (!disposedValue)
        {
            if (disposing)
            {
                MediaPlayerElement.Source = null;
                sources.Clear();
                mvid = null;
                mvquality = null;
                songid = null;
                _cancellationTokenSource.Dispose();
            }

            disposedValue = true;
        }
    }

    ~MVPage()
    {
        Dispose(disposing: false);
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}