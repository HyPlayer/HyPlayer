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

    private async Task LoadRelateive()
    {
        _cancellationToken.ThrowIfCancellationRequested();
        var result = await _richMediaProvider.GetRichMediaFeedAsync($"song:{_songId}", 0, 10, _cancellationToken);
        foreach (var item in result.Items) _sources.Add(await MapRichMediaToCardAsync(item));

        RelativeList.ItemsSource = _sources;

        RelativeList.SelectedIndex = 0;
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
        _isUnloaded = true;
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

    private async Task LoadVideo(int loadVersion, CancellationToken cancellationToken)
    {
        try
        {
            var mvId = _mvId;
            var quality = _mvQuality;
            cancellationToken.ThrowIfCancellationRequested();
            LoadingControl.IsLoading = true;
            var resource = await _richMediaProvider.GetRichMediaResourceAsync(
                mvId,
                GetRichMediaTypeId(mvId),
                quality,
                cancellationToken);
            var resourceResult = resource is null ? null : await resource.GetResourceAsync(ctk: cancellationToken);
            if (resourceResult is not IResourceResultOf<Uri?> uriResource)
            {
                if (!IsCurrentVideoLoad(loadVersion, cancellationToken)) return;
                _notification.ShowMessage("加载视频时出错", "视频资源为空");
                return;
            }

            var uri = await uriResource.GetResourceAsync(cancellationToken);
            if (!IsCurrentVideoLoad(loadVersion, cancellationToken)) return;
            if (uri is null)
            {
                _notification.ShowMessage("加载视频时出错", "视频地址为空");
                return;
            }

            ReleaseCurrentMediaSource();
            _currentMediaSource = MediaSource.CreateFromUri(uri);
            MediaPlayerElement.Source = _currentMediaSource;
            var mediaPlayer = MediaPlayerElement.MediaPlayer;
            mediaPlayer.Play();
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            if (IsCurrentVideoLoad(loadVersion, cancellationToken))
                LoadingControl.IsLoading = false;
        }
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

public sealed class RichMediaCardViewModel
{
    public string ActualId { get; init; } = string.Empty;
    public string? Name { get; init; }
    public string Description { get; init; } = string.Empty;
    public string CoverUrl { get; init; } = string.Empty;
}
