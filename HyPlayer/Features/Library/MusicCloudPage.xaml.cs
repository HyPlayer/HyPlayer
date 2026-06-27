#region

using CommunityToolkit.Mvvm.DependencyInjection;
using CommunityToolkit.WinUI.Helpers;
using HyPlayer.Domain.Music;
using HyPlayer.Domain.Settings;
using HyPlayer.Infrastructure.Netease;
using HyPlayer.PlayCore.Abstraction;
using HyPlayer.PlayCore.Abstraction.Interfaces.PlayListContainer;
using HyPlayer.PlayCore.Abstraction.Interfaces.Provider;
using HyPlayer.PlayCore.Abstraction.Models.Containers;
using HyPlayer.PlayCore.Abstraction.Models.SingleItems;
using HyPlayer.Services.Abstractions;
using HyPlayer.Services.Cache;
using HyPlayer.Services.Downloads;
using HyPlayer.UI.Lists;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage.Pickers;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;
#endregion

// https://go.microsoft.com/fwlink/?LinkId=234238 上介绍了“空白页”项模板

namespace HyPlayer.Features.Library;

/// <summary>
///     可用于自身或导航至 Frame 内部的空白页。
/// </summary>
public sealed partial class MusicCloudPage : Page
{
    private readonly Setting _setting = Ioc.Default.GetRequiredService<Setting>();
    private readonly INotificationService _notification = Ioc.Default.GetRequiredService<INotificationService>();
    private readonly PlayCoreBase _playCore = Ioc.Default.GetRequiredService<PlayCoreBase>();
    private readonly IUserLibraryProvidable _userLibraryProvider = Ioc.Default.GetRequiredService<IUserLibraryProvidable>();
    private readonly IUserLibraryTypeIds _userLibraryTypeIds = Ioc.Default.GetRequiredService<IUserLibraryTypeIds>();
    private readonly IGlobalTimerService _globalTimer = Ioc.Default.GetRequiredService<IGlobalTimerService>();
    private readonly WeakEventListener<MusicCloudPage, object?, EventArgs> _secondTickListener;
    private bool _isSecondTickSubscribed;

    private readonly ObservableCollection<SongListItemViewModel> Items = new();
    private int page;
    private CancellationTokenSource _cancellationTokenSource = new CancellationTokenSource();
    private CancellationToken _cancellationToken;
    private Task _loadResultTask;
    private readonly HashSet<int> _loadedPages = [];

    public MusicCloudPage()
    {
        InitializeComponent();
        _cancellationToken = _cancellationTokenSource.Token;
        _secondTickListener = new WeakEventListener<MusicCloudPage, object?, EventArgs>(this)
        {
            OnEventAction = static (instance, _, _) => instance.GreedlyLoad(),
            OnDetachAction = weakEventListener => { _globalTimer.SecondTick -= weakEventListener.OnEvent; },
        };
    }

    public async Task LoadMusicCloudItem()
    {
        var currentPage = page;
        if (!_loadedPages.Add(currentPage))
            return;

        if (_loadResultTask is { IsCompleted: false })
            await _loadResultTask;

        _cancellationToken.ThrowIfCancellationRequested();
        var jv = await SimpleCacher.GetOrCreateCacheAsync(CacheType.Login, "userCloud_" + currentPage, async () =>
        {
            try
            {
                if (await _userLibraryProvider.GetCurrentUserLibraryContainerAsync(_userLibraryTypeIds.CloudLibraryTypeId, _cancellationToken) is not IProgressiveLoadingContainer container)
                    return new CloudLibraryPage();

                var (hasMore, items) = await container.GetProgressiveItemsListAsync(currentPage * 749, 749, _cancellationToken);
                return new CloudLibraryPage
                {
                    HasMore = hasMore,
                    Items = items.OfType<CloudLibraryItemBase>().ToList()
                };
            }
            catch (Exception ex)
            {
                treashold = ++cooldownTime * 10;
                page--;
                _loadedPages.Remove(currentPage);
                _notification.ShowMessage("贪婪加载被风控", $"渐进加载速度过于快, 将在 {cooldownTime * 10} 秒后尝试继续加载, 正在清洗请求: {ex.Message}");
                return null;
            }
        });




        var idx = currentPage * 749;
        foreach (var jToken in jv?.Items ?? [])
        {
            _cancellationToken.ThrowIfCancellationRequested();
            try
            {
                Items.Add(await MapCloudLibraryItemToRowAsync(jToken, idx++));
            }
            catch
            {
                //ignore
            }

            NextPage.Visibility = jv.HasMore ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private sealed class CloudLibraryPage
    {
        public bool HasMore { get; init; }
        public List<CloudLibraryItemBase> Items { get; init; } = [];
    }

    protected override async void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        DetachSecondTick();

        if (_loadResultTask != null && !_loadResultTask.IsCompleted)
        {
            try
            {
                _cancellationTokenSource.Cancel();
                await _loadResultTask;
            }
            catch
            {
                //Ignore
            }
        }

        _cancellationTokenSource?.Dispose();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        StartLoadCurrentPage();
        if (_setting.greedlyLoadPlayContainerItems)
            AttachSecondTick();
    }

    private void AttachSecondTick()
    {
        if (_isSecondTickSubscribed) return;
        _globalTimer.SecondTick += _secondTickListener.OnEvent;
        _isSecondTickSubscribed = true;
    }

    private void DetachSecondTick()
    {
        if (!_isSecondTickSubscribed) return;
        _secondTickListener.Detach();
        _isSecondTickSubscribed = false;
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

            if (Items.Count > 0 && NextPage.Visibility == Visibility.Visible && treashold-- <= 0)
            {
                NextPage_OnClickPage_OnClick(null, null);
                treashold = 3;
            }
            else if (Items.Count > 0 && NextPage.Visibility == Visibility.Collapsed)
            {
                DetachSecondTick();
                OnLoadedAllSongs();
            }
        });
    }

    public void OnLoadedAllSongs()
    {
        if (_setting.AutoAddGreedilyLoadedSongsToPlayList && _playCore.PlaySourceId == "Content")
        {
            _ = _playCore.InsertSongRangeAsync(Items.Select(song => song.ToProviderSong()).ToList());
        }
    }

    private void NextPage_OnClickPage_OnClick(object sender, RoutedEventArgs e)
    {
        if (_loadResultTask is { IsCompleted: false })
            return;

        page++;
        StartLoadCurrentPage();
    }

    private void ButtonDownloadAll_OnClick(object sender, RoutedEventArgs e)
    {
        DownloadManager.AddDownload(Items.Select(song => song.ToProviderSong()).ToList());
    }

    private async void BtnUpload_Click(object sender, RoutedEventArgs e)
    {
        var fop = new FileOpenPicker();
        fop.FileTypeFilter.Add(".flac");
        fop.FileTypeFilter.Add(".mp3");
        fop.FileTypeFilter.Add(".ncm");
        fop.FileTypeFilter.Add(".ape");
        fop.FileTypeFilter.Add(".m4a");
        fop.FileTypeFilter.Add(".wav");


        var files =
            await fop.PickMultipleFilesAsync();
        if (files == null) return;
        _notification.ShowMessage("请稍等", "正在上传 " + files.Count + " 个音乐文件");
        for (var i = 0; i < files.Count; i++)
        {
            _notification.ShowMessage("正在上传共 " + files.Count + " 个音乐文件", "正在上传 第" + i + " 个音乐文件");
            await CloudUpload.UploadMusic(files[i]);
        }

        _notification.ShowMessage("上传完成", "请重新加载云盘页面");
    }
    private async void BtnRefresh_OnClick(object sender, RoutedEventArgs e)
    {
        await SimpleCacher.ResetCacheAsync(CacheType.Login, "userCloud_", true);
        Items.Clear();
        _loadedPages.Clear();
        page = 0;
        StartLoadCurrentPage();
    }

    private void StartLoadCurrentPage()
    {
        _loadResultTask = LoadMusicCloudItem();
    }

    private static async Task<SongListItemViewModel> MapCloudLibraryItemToRowAsync(CloudLibraryItemBase item, int order)
    {
        return await SongListItemViewModel.FromProviderSongAsync(item, order, isCloud: true);
    }
}
