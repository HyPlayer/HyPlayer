#region

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media.Imaging;
using Windows.UI.Xaml.Navigation;
using CommunityToolkit.Mvvm.DependencyInjection;
using HyPlayer.Application.Notifications;
using HyPlayer.Domain.Music;
using HyPlayer.Domain.Settings;
using HyPlayer.Features.Downloads.Services;
using HyPlayer.Features.Playback.Services;
using HyPlayer.Features.User;
using HyPlayer.PlayCore.Abstraction.Interfaces.PlayListContainer;
using HyPlayer.PlayCore.Abstraction.Interfaces.ProvidableItem;
using HyPlayer.PlayCore.Abstraction.Interfaces.Provider;
using HyPlayer.PlayCore.Abstraction.Models;
using HyPlayer.PlayCore.Abstraction.Models.Containers;
using HyPlayer.PlayCore.Abstraction.Models.SingleItems;
using HyPlayer.Shell.Navigation.Services;
using HyPlayer.UI.Lists;

#endregion

namespace HyPlayer.Features.Radio;

public sealed partial class RadioPage : Page
{
    public static readonly DependencyProperty CurrentContainerProperty = DependencyProperty.Register(
        nameof(CurrentContainer), typeof(ContainerBase), typeof(RadioPage),
        new PropertyMetadata(default(ContainerBase)));

    public static readonly DependencyProperty CurrentQueueScopeProperty = DependencyProperty.Register(
        nameof(CurrentQueueScope), typeof(SongListQueueScope), typeof(RadioPage),
        new PropertyMetadata(SongListQueueScope.Visible));

    private readonly CancellationToken _cancellationToken;
    private readonly CancellationTokenSource _cancellationTokenSource = new();

    private readonly IProvidableItemProvidable _itemProvider =
        Ioc.Default.GetRequiredService<IProvidableItemProvidable>();

    private readonly IProviderKnownTypeIds _knownTypeIds = Ioc.Default.GetRequiredService<IProviderKnownTypeIds>();
    private readonly INavigationService _navigation = Ioc.Default.GetRequiredService<INavigationService>();
    private readonly INotificationService _notification = Ioc.Default.GetRequiredService<INotificationService>();
    private readonly IPlaybackQueueLoader _queueLoader = Ioc.Default.GetRequiredService<IPlaybackQueueLoader>();
    private readonly Setting _setting = Ioc.Default.GetRequiredService<Setting>();
    private List<SingleSongBase> _allPrograms;
    private List<SingleSongBase> _ascendingPrograms;
    private PersonBase _host;
    private IProgressiveLoadingContainer _progressiveRadioChannel;

    private bool asc;
    private ContainerBase RadioChannel;

    public RadioPage()
    {
        InitializeComponent();
        _cancellationToken = _cancellationTokenSource.Token;
    }

    public ContainerBase CurrentContainer
    {
        get => (ContainerBase)GetValue(CurrentContainerProperty);
        set => SetValue(CurrentContainerProperty, value);
    }

    public SongListQueueScope CurrentQueueScope
    {
        get => (SongListQueueScope)GetValue(CurrentQueueScopeProperty);
        set => SetValue(CurrentQueueScopeProperty, value);
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        _cancellationTokenSource.Cancel();
        _cancellationTokenSource?.Dispose();
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is string rid)
        {
            RadioChannel = await GetRadioChannelAsync(rid);
            if (RadioChannel is null)
            {
                _notification.ShowMessage("获取电台信息失败", "未知错误");
                return;
            }
        }

        if (e.Parameter is ContainerBase radio) RadioChannel = radio;

        _progressiveRadioChannel = RadioChannel as IProgressiveLoadingContainer;
        if (_progressiveRadioChannel is null)
        {
            _notification.ShowMessage("获取电台信息失败", "提供程序未返回可分页电台容器");
            return;
        }

        TextBoxRadioName.Text = RadioChannel.Name;
        var creators = RadioChannel is IHasCreators creatorsProvider
            ? await creatorsProvider.GetCreatorsAsync(_cancellationToken)
            : null;
        _host = creators?.FirstOrDefault();
        TextBoxDJ.Content = _host?.Name;
        TextBlockDesc.Text = RadioChannel is IHasDescription descriptionProvider
            ? descriptionProvider.Description
            : string.Empty;
        if (_setting.noImage)
        {
            ImageRect.ImageSource = null;
        }
        else
        {
            var img = new BitmapImage();
            ImageRect.ImageSource = img;
            img.UriSource = await GetCoverUriAsync(RadioChannel);
        }

        _ascendingPrograms = null;
        _allPrograms = null;
        CurrentQueueScope = SongListQueueScope.Radio(RadioChannel.ActualId);
        CurrentContainer = RadioChannel;
        SongContainer.GreedyLoad = _setting.greedlyLoadPlayContainerItems;
    }

    private async void ButtonPlayAll_OnClick(object sender, RoutedEventArgs e)
    {
        await SongContainer.PlayAllAsync();
    }

    private void TextBoxDJ_OnTapped(object sender, RoutedEventArgs routedEventArgs)
    {
        if (!string.IsNullOrWhiteSpace(_host?.ActualId))
            _navigation.Navigate(typeof(Me), _host.ActualId);
    }

    private void Button_Click(object sender, RoutedEventArgs e)
    {
        asc = !asc;
        _ascendingPrograms = null;
        CurrentContainer = asc ? new ReorderedContainer(RadioChannel, true) : RadioChannel;
    }

    private async void BtnAddAll_Clicked(object sender, RoutedEventArgs e)
    {
        var programs = asc
            ? await LoadAscendingProgramsAsync()
            : await LoadAllProgramsAsync();
        await _queueLoader.AppendSongsAsync(programs);
    }

    private async void ButtonDownloadAll_OnClick(object sender, RoutedEventArgs e)
    {
        var programs = asc
            ? await LoadAscendingProgramsAsync()
            : await LoadAllProgramsAsync();

        DownloadManager.AddDownload(programs);
    }

    private async Task<ContainerBase> GetRadioChannelAsync(string radioId)
    {
        if (_knownTypeIds.RadioChannelTypeId is null)
            return null;

        return await _itemProvider.GetProvidableItemByIdAsync(_knownTypeIds.RadioChannelTypeId + radioId,
            _cancellationToken) as ContainerBase;
    }

    private async Task<List<SingleSongBase>> LoadAscendingProgramsAsync()
    {
        if (_ascendingPrograms is not null) return _ascendingPrograms;

        var programs = await LoadAllProgramsAsync();
        programs.Reverse();
        _ascendingPrograms = programs;
        return _ascendingPrograms;
    }

    private async Task<List<SingleSongBase>> LoadAllProgramsAsync()
    {
        if (_allPrograms is not null) return _allPrograms;

        var programs = RadioChannel is LinerContainerBase liner
            ? await liner.GetAllItemsAsync(_cancellationToken)
            : (await _progressiveRadioChannel.GetProgressiveItemsListAsync(0,
                _progressiveRadioChannel.MaxProgressiveCount, _cancellationToken)).Item2;
        _allPrograms = programs.OfType<SingleSongBase>().ToList();
        return _allPrograms;
    }

    private static async Task<Uri?> GetCoverUriAsync(ContainerBase container)
    {
        if (container is not IHasCover coverProvider)
            return null;

        var result = await coverProvider.GetCoverAsync();
        return result is IResourceResultOf<Uri?> uriResult ? await uriResult.GetResourceAsync() : null;
    }
}