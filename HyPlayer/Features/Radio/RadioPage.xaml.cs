#region

using CommunityToolkit.Mvvm.DependencyInjection;
using HyPlayer.Domain;
using HyPlayer.Domain.Music;
using HyPlayer.Domain.Settings;
using HyPlayer.Features.User;
using HyPlayer.NeteaseProvider.Models;
using HyPlayer.PlayCore.Abstraction;
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
    private readonly global::HyPlayer.NeteaseProvider.NeteaseProvider _neteaseProvider = Ioc.Default.GetRequiredService<global::HyPlayer.NeteaseProvider.NeteaseProvider>();
    private readonly IGlobalTimerService _globalTimer = Ioc.Default.GetRequiredService<IGlobalTimerService>();
    private readonly WeakEventListener<RadioPage, object?, EventArgs> _secondTickListener;
    private bool _isSecondTickSubscribed;
    private readonly INotificationService _notification = Ioc.Default.GetRequiredService<INotificationService>();
    private readonly INavigationService _navigation = Ioc.Default.GetRequiredService<INavigationService>();
    private readonly IAppNavigator _navigator = Ioc.Default.GetRequiredService<IAppNavigator>();
    private readonly PlayCoreBase _playCore = Ioc.Default.GetRequiredService<PlayCoreBase>();
    private readonly IPlaybackQueueLoader _queueLoader = Ioc.Default.GetRequiredService<IPlaybackQueueLoader>();
    private readonly IPlaybackControlService _control = Ioc.Default.GetRequiredService<IPlaybackControlService>();

    private bool asc;
    private int i;
    private int page;
    private NeteaseRadioChannel RadioChannel;
    private List<NeteaseRadioProgram> _ascendingPrograms;
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
            Songs.Add(SongListItemViewModel.FromRadioProgram(program, i++));
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

        if (e.Parameter is NeteaseRadioChannel radio)
        {
            RadioChannel = radio;
        }

        TextBoxRadioName.Text = RadioChannel.Name;
        TextBoxDJ.Content = RadioChannel.Host?.Name;
        TextBlockDesc.Text = RadioChannel.Description;
        if (_setting.noImage)
        {
            ImageRect.ImageSource = null;
        }
        else
        {
            var img = new BitmapImage();
            ImageRect.ImageSource = img;
            img.UriSource = new Uri(RadioChannel.CoverUrl + "?param=" + StaticSource.PICSIZE_SONGLIST_DETAIL_COVER);
        }

        Songs.Clear();
        _ascendingPrograms = null;
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
        await _navigator.AppendAsync(new MusicResource.Radio(RadioChannel.ActualId));
        if (asc) await _playCore.ReversePlaylistAsync();
        await _playCore.MovePointerToIndexAsync(0);
        if (_playCore.CurrentSong is { } song)
            await _control.LoadAndPlayAsync(song, removeCurrentSongs: false);
    }

    private void TextBoxDJ_OnTapped(object sender, RoutedEventArgs routedEventArgs)
    {
        _navigation.Navigate(typeof(Me), RadioChannel.Host?.ActualId);
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
        await _queueLoader.AppendRadioListAsync(RadioChannel.ActualId, asc);
    }

    private async void ButtonDownloadAll_OnClick(object sender, RoutedEventArgs e)
    {
        var programs = asc
            ? await LoadAscendingProgramsAsync()
            : await LoadAllProgramsAsync();

        DownloadManager.AddDownload(programs.Cast<SingleSongBase>().ToList());
    }

    private async Task<NeteaseRadioChannel> GetRadioChannelAsync(string radioId)
    {
        return await _neteaseProvider.GetProvidableItemByIdAsync(global::HyPlayer.NeteaseProvider.Constants.NeteaseTypeIds.RadioChannel + radioId, _cancellationToken) as NeteaseRadioChannel;
    }

    private async Task<(bool HasMore, List<NeteaseRadioProgram> Programs)> LoadProgramPageAsync(int pageIndex, bool ascending)
    {
        if (ascending)
        {
            var allPrograms = await LoadAscendingProgramsAsync();
            var programs = allPrograms.Skip(pageIndex * 100).Take(100).ToList();
            return ((pageIndex + 1) * 100 < allPrograms.Count, programs);
        }

        var (hasMore, items) = await RadioChannel.GetProgressiveItemsListAsync(pageIndex * 100, 100, _cancellationToken);
        return (hasMore, items.OfType<NeteaseRadioProgram>().ToList());
    }

    private async Task<List<NeteaseRadioProgram>> LoadAscendingProgramsAsync()
    {
        if (_ascendingPrograms is not null) return _ascendingPrograms;

        var programs = await LoadAllProgramsAsync();
        programs.Reverse();
        _ascendingPrograms = programs;
        return _ascendingPrograms;
    }

    private async Task<List<NeteaseRadioProgram>> LoadAllProgramsAsync()
    {
        var programs = await RadioChannel.GetAllItemsAsync(_cancellationToken);
        return programs.OfType<NeteaseRadioProgram>().ToList();
    }

}
