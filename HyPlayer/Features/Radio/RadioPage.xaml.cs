#region

using CommunityToolkit.Mvvm.DependencyInjection;
using HyPlayer.Domain;
using HyPlayer.Domain.Music;
using HyPlayer.Domain.Settings;
using HyPlayer.Features.User;
using HyPlayer.PlayCore.Abstraction;
using HyPlayer.PlayCore.Abstraction.Interfaces.PlayListContainer;
using HyPlayer.PlayCore.Abstraction.Interfaces.ProvidableItem;
using HyPlayer.PlayCore.Abstraction.Interfaces.Provider;
using HyPlayer.PlayCore.Abstraction.Models;
using HyPlayer.PlayCore.Abstraction.Models.Containers;
using HyPlayer.PlayCore.Abstraction.Models.SingleItems;
using HyPlayer.Services.Abstractions;
using HyPlayer.Services.Downloads;
using HyPlayer.UI.Lists;
using CommunityToolkit.WinUI.Helpers;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media.Imaging;
using Windows.UI.Xaml.Navigation;

#endregion

namespace HyPlayer.Features.Radio;

public sealed partial class RadioPage : Page
{
    private readonly Setting _setting = Ioc.Default.GetRequiredService<Setting>();
    private readonly IProvidableItemProvidable _itemProvider = Ioc.Default.GetRequiredService<IProvidableItemProvidable>();
    private readonly IProviderKnownTypeIds _knownTypeIds = Ioc.Default.GetRequiredService<IProviderKnownTypeIds>();
    private readonly IGlobalTimerService _globalTimer = Ioc.Default.GetRequiredService<IGlobalTimerService>();
    private readonly WeakEventListener<RadioPage, object?, EventArgs> _secondTickListener;
    private bool _isSecondTickSubscribed;
    private readonly INotificationService _notification = Ioc.Default.GetRequiredService<INotificationService>();
    private readonly INavigationService _navigation = Ioc.Default.GetRequiredService<INavigationService>();
    private readonly PlayCoreBase _playCore = Ioc.Default.GetRequiredService<PlayCoreBase>();
    private readonly IPlaybackQueueLoader _queueLoader = Ioc.Default.GetRequiredService<IPlaybackQueueLoader>();
    private readonly IPlaybackControlService _control = Ioc.Default.GetRequiredService<IPlaybackControlService>();

    private bool asc;
    private int i;
    private int page;
    private ContainerBase RadioChannel;
    private IProgressiveLoadingContainer _progressiveRadioChannel;
    private PersonBase _host;
    private List<SingleSongBase> _ascendingPrograms;
    private List<SingleSongBase> _allPrograms;
    private Task _programLoaderTask;
    private CancellationTokenSource _cancellationTokenSource = new CancellationTokenSource();
    private CancellationToken _cancellationToken;

    public ObservableCollection<SongListItemViewModel> Songs = new();

    public RadioPage()
    {
        InitializeComponent();
        _cancellationToken = _cancellationTokenSource.Token;
        _secondTickListener = new WeakEventListener<RadioPage, object?, EventArgs>(this)
        {
            OnEventAction = static (instance, _, _) => instance.GreedlyLoad(),
            OnDetachAction = weakEventListener => {_globalTimer.SecondTick -= weakEventListener.OnEvent; }
        };
    }

    protected override async void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        DetachSecondTick();

        if (_programLoaderTask != null && !_programLoaderTask.IsCompleted)
        {
            try
            {
                _cancellationTokenSource.Cancel();
                await _programLoaderTask;
            }
            catch
            {
                //Ignore
            }
        }

        _cancellationTokenSource?.Dispose();
    }

    private async Task LoadProgram()
    {
        _cancellationToken.ThrowIfCancellationRequested();

        var (hasMore, programs) = await LoadProgramPageAsync(page, asc);

        NextPage.Visibility = hasMore ? Visibility.Visible : Visibility.Collapsed;
        foreach (var program in programs)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            Songs.Add(await SongListItemViewModel.FromRadioProgramAsync(program, i++));
        }
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

        if (e.Parameter is ContainerBase radio)
        {
            RadioChannel = radio;
        }

        _progressiveRadioChannel = RadioChannel as IProgressiveLoadingContainer;
        if (_progressiveRadioChannel is null)
        {
            _notification.ShowMessage("获取电台信息失败", "提供程序未返回可分页电台容器");
            return;
        }

        TextBoxRadioName.Text = RadioChannel.Name;
        var creators = RadioChannel is IHasCreators creatorsProvider ? await creatorsProvider.GetCreatorsAsync(_cancellationToken) : null;
        _host = creators?.FirstOrDefault();
        TextBoxDJ.Content = _host?.Name;
        TextBlockDesc.Text = RadioChannel is IHasDescription descriptionProvider ? descriptionProvider.Description : string.Empty;
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

        Songs.Clear();
        _ascendingPrograms = null;
        _allPrograms = null;
        SongContainer.QueueScope = SongListQueueScope.Radio(RadioChannel.ActualId);
        _programLoaderTask = LoadProgram();
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

    private void GreedlyLoad()
    {
        _ = _notification.InvokeOnUIThread(() =>
        {
            if (treashold > 10)
            {
                treashold--;
                return;
            }

            if (Songs.Count > 0 && NextPage.Visibility == Visibility.Visible && treashold-- <= 0)
            {
                NextPage_OnClickPage_OnClick(null, null);
                treashold = 3;
            }
            else if (Songs.Count > 0 && NextPage.Visibility == Visibility.Collapsed)
            {
                DetachSecondTick();
            }
        });
    }

    private void NextPage_OnClickPage_OnClick(object sender, RoutedEventArgs e)
    {
        page++;
        _programLoaderTask = LoadProgram();
    }

    private async void ButtonPlayAll_OnClick(object sender, RoutedEventArgs e)
    {
        await _playCore.StopAsync();
        await _playCore.RemoveAllSongAsync();
        var programs = asc
            ? await LoadAscendingProgramsAsync()
            : await LoadAllProgramsAsync();
        await _playCore.InsertSongRangeAsync(programs);
        await _playCore.MovePointerToIndexAsync(0);
        if (_playCore.CurrentSong is { } song)
            await _control.LoadAndPlayAsync(song, removeCurrentSongs: false);
    }

    private void TextBoxDJ_OnTapped(object sender, RoutedEventArgs routedEventArgs)
    {
        if (!string.IsNullOrWhiteSpace(_host?.ActualId))
            _navigation.Navigate(typeof(Me), _host.ActualId);
    }

    private void Button_Click(object sender, RoutedEventArgs e)
    {
        Songs.Clear();
        page = 0;
        i = 0;
        asc = !asc;
        _ascendingPrograms = null;
        _programLoaderTask = LoadProgram();
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

        return await _itemProvider.GetProvidableItemByIdAsync(_knownTypeIds.RadioChannelTypeId + radioId, _cancellationToken) as ContainerBase;
    }

    private async Task<(bool HasMore, List<SingleSongBase> Programs)> LoadProgramPageAsync(int pageIndex, bool ascending)
    {
        if (ascending)
        {
            var allPrograms = await LoadAscendingProgramsAsync();
            var programs = allPrograms.Skip(pageIndex * 100).Take(100).ToList();
            return ((pageIndex + 1) * 100 < allPrograms.Count, programs);
        }

        var (hasMore, items) = await _progressiveRadioChannel.GetProgressiveItemsListAsync(pageIndex * 100, 100, _cancellationToken);
        return (hasMore, items.OfType<SingleSongBase>().ToList());
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
            : (await _progressiveRadioChannel.GetProgressiveItemsListAsync(0, _progressiveRadioChannel.MaxProgressiveCount, _cancellationToken)).Item2;
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
