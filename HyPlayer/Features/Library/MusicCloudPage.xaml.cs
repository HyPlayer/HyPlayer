#region

using CommunityToolkit.Mvvm.DependencyInjection;
using CommunityToolkit.WinUI.Helpers;
using HyPlayer.Domain.Music;
using HyPlayer.Domain.Settings;
using HyPlayer.Infrastructure.Netease;
using HyPlayer.PlayCore.Abstraction.Interfaces.PlayListContainer;
using HyPlayer.PlayCore.Abstraction.Interfaces.Provider;
using HyPlayer.PlayCore.Abstraction.Models;
using HyPlayer.PlayCore.Abstraction.Models.Containers;
using HyPlayer.Services.Abstractions;
using HyPlayer.Services.Cache;
using HyPlayer.Services.Downloads;
using HyPlayer.UI.Lists;
using System;
using System.Collections.Generic;
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
    private readonly IUserLibraryProvidable _userLibraryProvider = Ioc.Default.GetRequiredService<IUserLibraryProvidable>();
    private readonly IUserLibraryTypeIds _userLibraryTypeIds = Ioc.Default.GetRequiredService<IUserLibraryTypeIds>();
    private readonly IContainerItemManagementProvidable _containerItemManagement = Ioc.Default.GetRequiredService<IContainerItemManagementProvidable>();

    private CancellationTokenSource _cancellationTokenSource = new CancellationTokenSource();
    private CancellationToken _cancellationToken;

    public static readonly DependencyProperty CloudContainerProperty = DependencyProperty.Register(
        nameof(CloudContainer), typeof(ContainerBase), typeof(MusicCloudPage), new PropertyMetadata(default(ContainerBase)));

    public MusicCloudPage()
    {
        ItemActions =
        [
            new ProvidableItemAction
            {
                Text = "从云盘删除",
                ExecuteAsync = DeleteCloudItemAsync
            }
        ];
        InitializeComponent();
        _cancellationToken = _cancellationTokenSource.Token;
    }

    public ContainerBase CloudContainer
    {
        get => (ContainerBase)GetValue(CloudContainerProperty);
        set => SetValue(CloudContainerProperty, value);
    }

    public bool GreedyLoad => _setting.greedlyLoadPlayContainerItems;
    public List<ProvidableItemAction> ItemActions { get; }

    protected override async void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        _cancellationTokenSource.Cancel();
        _cancellationTokenSource?.Dispose();
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        await LoadCloudContainerAsync();
    }

    private void ButtonDownloadAll_OnClick(object sender, RoutedEventArgs e)
    {
        SongContainer.DownloadAllLoaded();
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
        await LoadCloudContainerAsync();
    }

    private async Task LoadCloudContainerAsync()
    {
        if (await _userLibraryProvider.GetCurrentUserLibraryContainerAsync(_userLibraryTypeIds.CloudLibraryTypeId, _cancellationToken) is ContainerBase container)
        {
            CloudContainer = container;
            Bindings.Update();
        }
    }

    private async Task DeleteCloudItemAsync(ProvidableItemRowViewModel row)
    {
        if (string.IsNullOrWhiteSpace(row.ItemId))
            return;

        try
        {
            await _containerItemManagement.RemoveItemFromContainerAsync(_userLibraryTypeIds.CloudLibraryTypeId, row.ItemId, _cancellationToken);
            await SimpleCacher.ResetCacheAsync(CacheType.Login, "userCloud_", true);
            _notification.ShowMessage("已从云盘删除", row.Title);
            await LoadCloudContainerAsync();
            SongContainer.ResetAndLoad();
        }
        catch (Exception ex)
        {
            _notification.ShowMessage("删除云盘歌曲失败", ex.Message);
        }
    }
}
