#region

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Windows.Media.Core;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;
using CommunityToolkit.Mvvm.DependencyInjection;
using HyPlayer.Application.Loading;
using HyPlayer.Application.Notifications;
using HyPlayer.Domain.Comments;
using HyPlayer.Features.Playback.Services;
using HyPlayer.PlayCore.Abstraction.Interfaces.ProvidableItem;
using HyPlayer.PlayCore.Abstraction.Interfaces.Provider;
using HyPlayer.PlayCore.Abstraction.Models;
using HyPlayer.PlayCore.Abstraction.Models.Containers;
using HyPlayer.PlayCore.Abstraction.Models.SingleItems;
using HyPlayer.UI.Lists;

#endregion

// https://go.microsoft.com/fwlink/?LinkId=234238 上介绍了“空白页”项模板

namespace HyPlayer.Features.Video;

/// <summary>
///     可用于自身或导航至 Frame 内部的空白页。
/// </summary>
public sealed partial class MVPage : Page
{
    private readonly CancellationToken _cancellationToken;
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private readonly IProviderKnownTypeIds _knownTypeIds = Ioc.Default.GetRequiredService<IProviderKnownTypeIds>();
    private readonly INotificationService _notification = Ioc.Default.GetRequiredService<INotificationService>();
    private readonly IRichMediaProvidable _richMediaProvider = Ioc.Default.GetRequiredService<IRichMediaProvidable>();

    private readonly IProviderSearchCategoryTypeIds _searchCategoryTypeIds =
        Ioc.Default.GetRequiredService<IProviderSearchCategoryTypeIds>();

    private readonly List<RichMediaCardViewModel> _sources = new();

    private readonly LoadState _videoLoad = new("视频", Ioc.Default.GetRequiredService<ILoadEnvironment>());

    private MediaSource _currentMediaSource;
    private bool _isUnloaded;
    private Task _relateiveLoaderTask;
    private bool _updatingQualitySelection;
    private Task _videoInfoLoaderTask;
    private CancellationTokenSource _videoLoadCancellationTokenSource = new();
    private Task _videoLoaderTask;
    private int _videoLoadVersion;
    private string _mvId;
    private string _mvQuality = "1080";
    private string _songId;

    public MVPage()
    {
        InitializeComponent();
        _cancellationToken = _cancellationTokenSource.Token;
        VideoLoadView.State = _videoLoad;
        _videoLoad.PropertyChanged += (_, _) => LoadingControl.IsLoading = _videoLoad.IsVisible;
        LoadingControl.IsLoading = _videoLoad.IsVisible;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is ProvidableItemRowViewModel row)
        {
            _mvId = row.RichMediaId;
            _songId = row.ActualId;
            _relateiveLoaderTask = LoadRelateive();
        }
        else if (e.Parameter is SingleSongBase providerSong)
        {
            _mvId = e.Parameter.ToString();
            _songId = providerSong.ActualId;
            LoadThings();
        }
        else
        {
            _mvId = e.Parameter.ToString();
            LoadThings();
        }
    }

    private void LoadThings()
    {
        if (_isUnloaded) return;

        Ioc.Default.GetRequiredService<IPlaybackControlService>().Pause();
        var token = ResetVideoLoad(out var loadVersion);
        _videoLoaderTask = LoadVideo(loadVersion, token);
        _videoInfoLoaderTask = LoadVideoInfo(loadVersion, token);
        LoadComment();
    }

    private Task LoadRelateive()
    {
        _videoLoad.EmptyText = "没有找到相关视频";
        return _videoLoad.RunAsync(async cancellationToken =>
        {
            var result = await _richMediaProvider.GetRichMediaFeedAsync($"song:{_songId}", 0, 10,
                cancellationToken);
            var cards = new List<RichMediaCardViewModel>();
            foreach (var item in result.Items) cards.Add(await MapRichMediaToCardAsync(item));
            return cards;
        }, cards =>
        {
            _sources.Clear();
            _sources.AddRange(cards);
            RelativeList.ItemsSource = null;
            RelativeList.ItemsSource = _sources;
            if (_sources.Count > 0)
                RelativeList.SelectedIndex = 0;
        }, static cards => cards.Count == 0);
    }

    private void LoadComment()
    {
        if (Regex.IsMatch(_mvId, "^[0-9]*$"))
            CommentFrame.Navigate(typeof(Comments.Comments), CommentTarget.MV(_mvId));
        else
            CommentFrame.Navigate(typeof(Comments.Comments), CommentTarget.MLog(_mvId));
    }

    protected override async void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        Bindings.StopTracking();
        _isUnloaded = true;
        _videoLoad.Cancel();
        _cancellationTokenSource.Cancel();
        _videoLoadCancellationTokenSource.Cancel();
        ReleaseCurrentMediaSource();
        if (_relateiveLoaderTask != null && !_relateiveLoaderTask.IsCompleted)
            try
            {
                await _relateiveLoaderTask;
            }
            catch
            {
            }

        if (_videoLoaderTask != null && !_videoLoaderTask.IsCompleted)
            try
            {
                await _videoLoaderTask;
            }
            catch
            {
            }

        if (_videoInfoLoaderTask != null && !_videoInfoLoaderTask.IsCompleted)
            try
            {
                await _videoInfoLoaderTask;
            }
            catch
            {
            }

        _cancellationTokenSource?.Dispose();
        _videoLoadCancellationTokenSource?.Dispose();
    }

    private Task LoadVideo(int loadVersion, CancellationToken cancellationToken)
    {
        var mvId = _mvId;
        var quality = _mvQuality;
        _videoLoad.EmptyText = "没有可播放的视频资源";
        return _videoLoad.RunAsync(async loadToken =>
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(loadToken, cancellationToken);
            var resource = await _richMediaProvider.GetRichMediaResourceAsync(
                mvId,
                GetRichMediaTypeId(mvId),
                quality,
                linked.Token);
            var resourceResult = resource is null ? null : await resource.GetResourceAsync(ctk: linked.Token);
            return resourceResult is IResourceResultOf<Uri?> uriResource
                ? await uriResource.GetResourceAsync(linked.Token)
                : null;
        }, uri =>
        {
            if (uri is null || !IsCurrentVideoLoad(loadVersion, cancellationToken)) return;

            ReleaseCurrentMediaSource();
            _currentMediaSource = MediaSource.CreateFromUri(uri);
            MediaPlayerElement.Source = _currentMediaSource;
            var mediaPlayer = MediaPlayerElement.MediaPlayer;
            mediaPlayer.Play();
        }, static uri => uri is null);
    }

    private async Task LoadVideoInfo(int loadVersion, CancellationToken cancellationToken)
    {
        try
        {
            var mvId = _mvId;
            cancellationToken.ThrowIfCancellationRequested();
            if (MvIdRegex().IsMatch(mvId))
            {
                var richMedia =
                    await _richMediaProvider.GetRichMediaAsync(mvId, GetRichMediaTypeId(mvId), cancellationToken);
                if (!IsCurrentVideoLoad(loadVersion, cancellationToken)) return;
                if (richMedia is null)
                {
                    _notification.ShowMessage("加载视频信息时出错", "视频信息为空");
                    return;
                }

                await DisplayRichMediaAsync(richMedia, cancellationToken);
            }
            else
            {
                var richMedia =
                    await _richMediaProvider.GetRichMediaAsync(mvId, GetRichMediaTypeId(mvId), cancellationToken);
                if (!IsCurrentVideoLoad(loadVersion, cancellationToken)) return;
                if (richMedia is null)
                {
                    _notification.ShowMessage("加载视频信息时出错", "视频信息为空");
                    return;
                }

                await DisplayRichMediaAsync(richMedia, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            if (IsCurrentVideoLoad(loadVersion, cancellationToken))
                _notification.ShowFailure("加载视频信息失败", exception, cancellationToken);
        }
    }

    private void VideoQualityBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingQualitySelection)
            return;

        _mvQuality = VideoQualityBox.SelectedItem?.ToString();
        if (_isUnloaded || string.IsNullOrEmpty(_mvId)) return;

        var token = ResetVideoLoad(out var loadVersion);
        _videoLoaderTask = LoadVideo(loadVersion, token);
    }

    private void RelativeList_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (RelativeList.SelectedItem is not RichMediaCardViewModel item) return;

        _mvId = item.ActualId;
        LoadThings();
    }

    [GeneratedRegex("^[0-9]*$")]
    private static partial Regex MvIdRegex();

    private string GetRichMediaTypeId(string mvId)
    {
        if (MvIdRegex().IsMatch(mvId) && _knownTypeIds.RichMediaTypeId is not null)
            return _knownTypeIds.RichMediaTypeId;

        return _searchCategoryTypeIds.ShortVideoSearchTypeId ?? _knownTypeIds.RichMediaTypeId ?? string.Empty;
    }

    private async Task DisplayRichMediaAsync(RichMediaBase richMedia, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        TextBoxVideoName.Text = richMedia.Name;
        TextBoxSinger.Text = richMedia is IHasCreators creatorsProvider
            ? string.Join(" / ",
                (await creatorsProvider.GetCreatorsAsync(cancellationToken))?.Select(creator => creator.Name) ?? [])
            : string.Empty;
        TextBoxDesc.Text = richMedia is IHasDescription descriptionProvider
            ? descriptionProvider.Description
            : string.Empty;
        TextBoxOtherInfo.Text = string.Empty;
        _updatingQualitySelection = true;
        try
        {
            if (!VideoQualityBox.Items.Contains(_mvQuality))
                VideoQualityBox.Items.Add(_mvQuality);
            VideoQualityBox.SelectedItem = _mvQuality;
        }
        finally
        {
            _updatingQualitySelection = false;
        }
    }

    private CancellationToken ResetVideoLoad(out int loadVersion)
    {
        _videoLoadCancellationTokenSource.Cancel();
        _videoLoadCancellationTokenSource.Dispose();
        _videoLoadCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(_cancellationToken);
        loadVersion = ++_videoLoadVersion;
        ReleaseCurrentMediaSource();
        return _videoLoadCancellationTokenSource.Token;
    }

    private bool IsCurrentVideoLoad(int loadVersion, CancellationToken cancellationToken)
    {
        return !_isUnloaded
               && loadVersion == _videoLoadVersion
               && !cancellationToken.IsCancellationRequested;
    }

    private void ReleaseCurrentMediaSource()
    {
        MediaPlayerElement.MediaPlayer?.Pause();
        MediaPlayerElement.Source = null;
        _currentMediaSource?.Dispose();
        _currentMediaSource = null;
    }

    private static async Task<RichMediaCardViewModel> MapRichMediaToCardAsync(RichMediaBase item)
    {
        Uri? coverUri = null;
        if (item is IHasCover coverProvider)
        {
            var cover = await coverProvider.GetCoverAsync();
            if (cover is IResourceResultOf<Uri?> uriResult)
                coverUri = await uriResult.GetResourceAsync();
        }

        return new RichMediaCardViewModel
        {
            ActualId = item.ActualId ?? string.Empty,
            Name = item.Name,
            Description = item is IHasDescription descriptionProvider ? descriptionProvider.Description : string.Empty,
            CoverUrl = coverUri?.ToString() ?? string.Empty
        };
    }
}
