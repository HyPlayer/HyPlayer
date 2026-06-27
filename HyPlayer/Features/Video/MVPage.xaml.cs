#region

using CommunityToolkit.Mvvm.DependencyInjection;
using HyPlayer.Domain.Comments;
using HyPlayer.PlayCore.Abstraction.Interfaces.ProvidableItem;
using HyPlayer.PlayCore.Abstraction.Interfaces.Provider;
using HyPlayer.PlayCore.Abstraction.Models;
using HyPlayer.PlayCore.Abstraction.Models.Containers;
using HyPlayer.PlayCore.Abstraction.Models.Resources;
using HyPlayer.PlayCore.Abstraction.Models.SingleItems;
using HyPlayer.Services.Abstractions;
using HyPlayer.UI.Lists;
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

namespace HyPlayer.Features.Video;

/// <summary>
///     可用于自身或导航至 Frame 内部的空白页。
/// </summary>
public sealed partial class MVPage : Page
{
    private readonly IRichMediaProvidable _richMediaProvider = Ioc.Default.GetRequiredService<IRichMediaProvidable>();
    private readonly IProviderKnownTypeIds _knownTypeIds = Ioc.Default.GetRequiredService<IProviderKnownTypeIds>();
    private readonly IProviderSearchCategoryTypeIds _searchCategoryTypeIds = Ioc.Default.GetRequiredService<IProviderSearchCategoryTypeIds>();
    private readonly INotificationService _notification = Ioc.Default.GetRequiredService<INotificationService>();

    private readonly List<RichMediaCardViewModel> sources = new();
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
        else if (e.Parameter is SingleSongBase providerSong)
        {
            MVId = e.Parameter.ToString();
            songid = providerSong.ActualId;
            LoadThings();
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
            sources.Add(await MapRichMediaToCardAsync(item));
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
            GetRichMediaTypeId(),
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
            var richMedia = await _richMediaProvider.GetRichMediaAsync(MVId, GetRichMediaTypeId(), _cancellationToken);
            if (richMedia is null)
            {
                _notification.ShowMessage("加载视频信息时出错", "视频信息为空");
                return;
            }

            await DisplayRichMediaAsync(richMedia);
        }
        else
        {
            var richMedia = await _richMediaProvider.GetRichMediaAsync(MVId, GetRichMediaTypeId(), _cancellationToken);
            if (richMedia is null)
            {
                _notification.ShowMessage("加载视频信息时出错", "视频信息为空");
                return;
            }

            await DisplayRichMediaAsync(richMedia);
        }
    }

    private void VideoQualityBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        mvquality = VideoQualityBox.SelectedItem?.ToString();
        _videoLoaderTask = LoadVideo();
    }

    private void RelativeList_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        MVId = (RelativeList.SelectedItem as RichMediaCardViewModel)?.ActualId ?? string.Empty;
        LoadThings();
    }

    [GeneratedRegex("^[0-9]*$")]
    private static partial Regex MvIdRegex();

    private string GetRichMediaTypeId()
    {
        if (MvIdRegex().IsMatch(MVId) && _knownTypeIds.RichMediaTypeId is not null)
            return _knownTypeIds.RichMediaTypeId;

        return _searchCategoryTypeIds.ShortVideoSearchTypeId ?? _knownTypeIds.RichMediaTypeId ?? string.Empty;
    }

    private async Task DisplayRichMediaAsync(RichMediaBase richMedia)
    {
        TextBoxVideoName.Text = richMedia.Name;
        TextBoxSinger.Text = richMedia is IHasCreators creatorsProvider
            ? string.Join(" / ", (await creatorsProvider.GetCreatorsAsync(_cancellationToken))?.Select(creator => creator.Name) ?? [])
            : string.Empty;
        TextBoxDesc.Text = richMedia is IHasDescription descriptionProvider ? descriptionProvider.Description : string.Empty;
        TextBoxOtherInfo.Text = string.Empty;
        if (!VideoQualityBox.Items.Contains(mvquality))
            VideoQualityBox.Items.Add(mvquality);
        VideoQualityBox.SelectedItem = mvquality;
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

public sealed partial class RichMediaCardViewModel
{
    public string ActualId { get; init; } = string.Empty;
    public string? Name { get; init; }
    public string Description { get; init; } = string.Empty;
    public string CoverUrl { get; init; } = string.Empty;
}
