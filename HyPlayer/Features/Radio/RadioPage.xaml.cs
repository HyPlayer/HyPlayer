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
using HyPlayer.Application.Loading;
using HyPlayer.Application.Notifications;
using HyPlayer.Domain.Music;
using HyPlayer.Domain.Settings;
using HyPlayer.Features.Downloads.Services;
using HyPlayer.Features.Playback.QueueProviders;
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

    private readonly LoadState _load = new("电台", Ioc.Default.GetRequiredService<ILoadEnvironment>())
    {
        EmptyText = "没有找到这个电台"
    };

    private readonly INavigationService _navigation = Ioc.Default.GetRequiredService<INavigationService>();
    private readonly INotificationService _notification = Ioc.Default.GetRequiredService<INotificationService>();
    private readonly IPlaybackQueueLoader _queueLoader = Ioc.Default.GetRequiredService<IPlaybackQueueLoader>();
    private readonly ApiSettings _apiSettings = Ioc.Default.GetRequiredService<ApiSettings>();
    private readonly UISettings _uiSettings = Ioc.Default.GetRequiredService<UISettings>();
    private List<SingleSongBase> _allPrograms;
    private List<SingleSongBase> _ascendingPrograms;
    private PersonBase _host;
    private IProgressiveLoadingContainer _progressiveRadioChannel;

    private bool _asc;
    private ContainerBase _radioChannel;

    public RadioPage()
    {
        InitializeComponent();
        _cancellationToken = _cancellationTokenSource.Token;
        SongContainer.ContainerLoad = _load;
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
        _cancellationTokenSource.Dispose();
        SongContainer.ReleaseResources();
        Bindings.StopTracking();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _ = _load.RunAsync(cancellationToken => LoadRadioChannelAsync(e.Parameter, cancellationToken));
    }

    private async Task<bool> LoadRadioChannelAsync(object parameter, CancellationToken cancellationToken)
    {
        if (parameter is string rid)
        {
            _radioChannel = await GetRadioChannelAsync(rid, cancellationToken);
            if (_radioChannel is null)
                return false;
        }

        if (parameter is ContainerBase radio) _radioChannel = radio;

        _progressiveRadioChannel = _radioChannel as IProgressiveLoadingContainer;
        if (_progressiveRadioChannel is null)
            return false;

        TextBoxRadioName.Text = _radioChannel.Name;
        var creators = _radioChannel is IHasCreators creatorsProvider
            ? await creatorsProvider.GetCreatorsAsync(cancellationToken)
            : null;
        _host = creators?.FirstOrDefault();
        TextBoxDJ.Content = _host?.Name;
        TextBlockDesc.Text = _radioChannel is IHasDescription descriptionProvider
            ? descriptionProvider.Description
            : string.Empty;
        if (_uiSettings.NoImage)
        {
            ImageRect.ImageSource = null;
        }
        else
        {
            var img = new BitmapImage();
            ImageRect.ImageSource = img;
            img.UriSource = await GetCoverUriAsync(_radioChannel);
        }

        _ascendingPrograms = null;
        _allPrograms = null;
        CurrentQueueScope = SongListQueueScope.Radio(_radioChannel.ActualId);
        CurrentContainer = _radioChannel;
        SongContainer.GreedyLoad = _apiSettings.GreedilyLoadPlayContainerItems;
        return true;
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
        _asc = !_asc;
        _ascendingPrograms = null;
        CurrentContainer = _asc ? new ReorderedContainer(_radioChannel, true) : _radioChannel;
    }

    private async void BtnAddAll_Clicked(object sender, RoutedEventArgs e)
    {
        if (await LoadProgramsForActionAsync() is { } programs)
            await _queueLoader.AppendSongsAsync(programs);
    }

    private async void ButtonDownloadAll_OnClick(object sender, RoutedEventArgs e)
    {
        if (await LoadProgramsForActionAsync() is { } programs)
            DownloadManager.AddDownload(programs);
    }

    private async Task<List<SingleSongBase>> LoadProgramsForActionAsync()
    {
        if (_progressiveRadioChannel is null)
            return null;

        try
        {
            return _asc
                ? await LoadAscendingProgramsAsync()
                : await LoadAllProgramsAsync();
        }
        catch (Exception exception)
        {
            _notification.ShowFailure("加载节目失败", exception, _cancellationToken);
            return null;
        }
    }

    private async Task<ContainerBase> GetRadioChannelAsync(string radioId, CancellationToken cancellationToken)
    {
        if (_knownTypeIds.RadioChannelTypeId is null)
            return null;

        return await LoadRequestProbe.GuardAsync(
            () => _itemProvider.GetProvidableItemByIdAsync(_knownTypeIds.RadioChannelTypeId + radioId,
                cancellationToken),
            static item => item is not ContainerBase) as ContainerBase;
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

        var pages = await ContainerItemLoader.LoadProgressivePagesAsync(_progressiveRadioChannel,
            _progressiveRadioChannel.MaxProgressiveCount, _cancellationToken);
        _allPrograms = pages.SelectMany(page => page).OfType<SingleSongBase>().ToList();
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
