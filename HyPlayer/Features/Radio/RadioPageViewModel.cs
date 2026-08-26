using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HyPlayer.Application.Notifications;
using HyPlayer.Domain.Music;
using HyPlayer.Domain.Settings;
using HyPlayer.Features.Downloads.Services;
using HyPlayer.Features.Playback.Services;
using HyPlayer.Features.User;
using HyPlayer.PlayCore.Abstraction.Interfaces.PlayListContainer;
using HyPlayer.PlayCore.Abstraction.Interfaces.Provider;
using HyPlayer.PlayCore.Abstraction.Models;
using HyPlayer.PlayCore.Abstraction.Models.SingleItems;
using HyPlayer.PlayCore.Abstraction.Interfaces.ProvidableItem;
using HyPlayer.UI.Lists;
using HyPlayer.PlayCore.Abstraction.Models.Containers;
using HyPlayer.Shell.Navigation.Services;

namespace HyPlayer.Features.Radio;

public partial class RadioPageViewModel(
    IProvidableItemProvidable itemProvider,
    IProviderKnownTypeIds knownTypeIds,
    INavigationService navigation,
    INotificationService notification,
    IPlaybackQueueLoader queueLoader,
    IDownloadService downloadService,
    ApiSettings apiSettings,
    UISettings uiSettings) : ObservableObject
{
    private List<SingleSongBase>? _allPrograms;
    private List<SingleSongBase>? _ascendingPrograms;
    private PersonBase? _host;
    private IProgressiveLoadingContainer? _progressiveRadioChannel;
    private ContainerBase? _radioChannel;
    private CancellationToken _loadCancellationToken;

    [ObservableProperty] public partial Uri? CoverUri { get; set; }
    [ObservableProperty] public partial ContainerBase? CurrentContainer { get; set; }
    [ObservableProperty] public partial SongListQueueScope CurrentQueueScope { get; set; } = SongListQueueScope.Visible;
    [ObservableProperty] public partial string Description { get; set; } = string.Empty;
    [ObservableProperty] public partial string HostName { get; set; } = string.Empty;
    [ObservableProperty] public partial bool IsAscending { get; set; }
    [ObservableProperty] public partial string Name { get; set; } = string.Empty;

    public bool GreedyLoad => apiSettings.GreedilyLoadPlayContainerItems;

    public async Task LoadAsync(object? parameter, CancellationToken cancellationToken)
    {
        _loadCancellationToken = cancellationToken;
        _radioChannel = parameter switch
        {
            string radioId => await GetRadioChannelAsync(radioId, cancellationToken),
            ContainerBase radio => radio,
            _ => null
        };
        if (_radioChannel is null)
        {
            notification.ShowMessage("获取电台信息失败", "未知错误");
            return;
        }

        _progressiveRadioChannel = _radioChannel as IProgressiveLoadingContainer;
        if (_progressiveRadioChannel is null)
        {
            notification.ShowMessage("获取电台信息失败", "提供程序未返回可分页电台容器");
            return;
        }

        Name = _radioChannel.Name;
        var creators = _radioChannel is IHasCreators creatorsProvider
            ? await creatorsProvider.GetCreatorsAsync(cancellationToken)
            : null;
        _host = creators?.FirstOrDefault();
        HostName = _host?.Name ?? string.Empty;
        Description = _radioChannel is IHasDescription descriptionProvider
            ? descriptionProvider.Description ?? string.Empty
            : string.Empty;
        CoverUri = uiSettings.NoImage ? null : await GetCoverUriAsync(_radioChannel);
        _ascendingPrograms = null;
        _allPrograms = null;
        IsAscending = false;
        CurrentQueueScope = SongListQueueScope.Radio(_radioChannel.ActualId);
        CurrentContainer = _radioChannel;
    }

    [RelayCommand]
    private void NavigateToHost()
    {
        if (!string.IsNullOrWhiteSpace(_host?.ActualId))
            navigation.Navigate(typeof(Me), _host.ActualId);
    }

    [RelayCommand]
    private void ToggleOrder()
    {
        if (_radioChannel is null)
            return;
        IsAscending = !IsAscending;
        _ascendingPrograms = null;
        CurrentContainer = IsAscending ? new ReorderedContainer(_radioChannel, true) : _radioChannel;
    }

    [RelayCommand]
    private async Task AddAllAsync()
    {
        await queueLoader.AppendSongsAsync(await GetVisibleProgramsAsync());
    }

    [RelayCommand]
    private async Task DownloadAllAsync()
    {
        await downloadService.AddAsync(await GetVisibleProgramsAsync());
    }

    private Task<List<SingleSongBase>> GetVisibleProgramsAsync()
    {
        return IsAscending ? LoadAscendingProgramsAsync() : LoadAllProgramsAsync();
    }

    private async Task<ContainerBase?> GetRadioChannelAsync(
        string radioId,
        CancellationToken cancellationToken)
    {
        if (knownTypeIds.RadioChannelTypeId is null)
            return null;
        return await itemProvider.GetProvidableItemByIdAsync(
            knownTypeIds.RadioChannelTypeId + radioId,
            cancellationToken) as ContainerBase;
    }

    private async Task<List<SingleSongBase>> LoadAscendingProgramsAsync()
    {
        if (_ascendingPrograms is not null)
            return _ascendingPrograms;
        _ascendingPrograms = [.. await LoadAllProgramsAsync()];
        _ascendingPrograms.Reverse();
        return _ascendingPrograms;
    }

    private async Task<List<SingleSongBase>> LoadAllProgramsAsync()
    {
        if (_allPrograms is not null)
            return _allPrograms;
        if (_radioChannel is null || _progressiveRadioChannel is null)
            return [];

        var programs = _radioChannel is LinerContainerBase liner
            ? await liner.GetAllItemsAsync(_loadCancellationToken)
            : (await _progressiveRadioChannel.GetProgressiveItemsListAsync(
                0,
                _progressiveRadioChannel.MaxProgressiveCount,
                _loadCancellationToken)).Item2;
        _allPrograms = programs.OfType<SingleSongBase>().ToList();
        return _allPrograms;
    }

    private static async Task<Uri?> GetCoverUriAsync(ContainerBase container)
    {
        if (container is not IHasCover coverProvider)
            return null;
        var result = await coverProvider.GetCoverAsync();
        return result is IResourceResultOf<Uri?> uriResult
            ? await uriResult.GetResourceAsync()
            : null;
    }
}
