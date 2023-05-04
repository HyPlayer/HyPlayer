#region

using HyPlayer.Classes;
using HyPlayer.HyPlayControl;
using NeteaseCloudMusicApi;
using System;
using System.Collections.Generic;
using System.Linq;
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
    public bool IsDisposed = false;
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

    public void Dispose()
    {
        MediaPlayerElement.Source = null;
        sources.Clear();
        mvid = null;
        mvquality = null;
        songid = null;
        _cancellationTokenSource.Dispose();
        IsDisposed = true;
        GC.SuppressFinalize(this);
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
        HyPlayList.Player.Pause();
        _videoLoaderTask = LoadVideo();
        _videoInfoLoaderTask = LoadVideoInfo();
        LoadComment();
    }

    private async Task LoadRelateive()
    {
        if (IsDisposed) throw new ObjectDisposedException(nameof(MVPage));
        _cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var json = await Common.ncapi.RequestAsync(CloudMusicApiProviders.MlogRcmdFeedList,
                new Dictionary<string, object>
                {
                    { "id", mvid },
                    { "songid", songid }
                });
            foreach (var jToken in json["data"]["feeds"])
                sources.Add(NCMlog.CreateFromJson(jToken["resource"]["mlogBaseData"]));

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
        if (IsDisposed) throw new ObjectDisposedException(nameof(MVPage));
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
        if (IsDisposed) throw new ObjectDisposedException(nameof(MVPage));
        _cancellationToken.ThrowIfCancellationRequested();
        if (Regex.IsMatch(mvid, "^[0-9]*$"))
            //纯MV
            try
            {
                var json = await Common.ncapi.RequestAsync(CloudMusicApiProviders.MvUrl,
                    new Dictionary<string, object> { { "id", mvid }, { "r", mvquality } });

                MediaPlayerElement.Source = MediaSource.CreateFromUri(new Uri(json["data"]["url"].ToString()));
                var mediaPlayer = MediaPlayerElement.MediaPlayer;
                mediaPlayer.Play();
                LoadingControl.IsLoading = false;
            }
            catch (Exception ex)
            {
                Common.AddToTeachingTipLists(ex.Message, (ex.InnerException ?? new Exception()).Message);
            }
        else
            try
            {
                var json = await Common.ncapi.RequestAsync(CloudMusicApiProviders.MlogUrl,
                    new Dictionary<string, object>
                    {
                        { "id", mvid },
                        { "resolution", mvquality }
                    });

                MediaPlayerElement.Source =
                    MediaSource.CreateFromUri(new Uri(json["data"][mvid]["urlInfo"]["url"].ToString()));
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
        if (IsDisposed) throw new ObjectDisposedException(nameof(MVPage));
        _cancellationToken.ThrowIfCancellationRequested();
        if (Regex.IsMatch(mvid, "^[0-9]*$"))
        {
            try
            {
                var json = await Common.ncapi.RequestAsync(CloudMusicApiProviders.MvDetail,
                    new Dictionary<string, object> { { "mvid", mvid } });
                TextBoxVideoName.Text = json["data"]["name"].ToString();
                TextBoxSinger.Text = json["data"]["artistName"].ToString();
                TextBoxDesc.Text = json["data"]["desc"].ToString();
                TextBoxOtherInfo.Text =
                    $"发布时间: {json["data"]["publishTime"]}    播放量: {json["data"]["playCount"]}次    收藏量: {json["data"]["subCount"]}次";
                foreach (var br in json["data"]["brs"].ToArray()) VideoQualityBox.Items.Add(br["br"].ToString());
            }
            catch (Exception ex)
            {
                Common.AddToTeachingTipLists(ex.Message, (ex.InnerException ?? new Exception()).Message);
            }
        }
        else
        {
            var mbinfo = sources.Find(t => t.id == mvid);
            TextBoxVideoName.Text = mbinfo.title;
            TextBoxSinger.Text = mbinfo.id;
            TextBoxDesc.Text = mbinfo.description;
            TextBoxOtherInfo.Text = "";
        }
    }

    private void VideoQualityBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (IsDisposed) throw new ObjectDisposedException(nameof(MVPage));
        mvquality = VideoQualityBox.SelectedItem.ToString();
        _videoLoaderTask = LoadVideo();
    }

    private void RelativeList_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (IsDisposed) throw new ObjectDisposedException(nameof(MVPage));
        mvid = (RelativeList.SelectedItem is NCMlog ? (NCMlog)RelativeList.SelectedItem : default).id;
        LoadThings();
    }
}