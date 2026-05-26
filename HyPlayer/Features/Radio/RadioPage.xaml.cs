#region

using CommunityToolkit.Mvvm.DependencyInjection;
using HyPlayer.Domain;
using HyPlayer.Domain.Music;
using HyPlayer.Domain.Settings;
using HyPlayer.Features.User;
using HyPlayer.Infrastructure.Netease;
using HyPlayer.NeteaseProvider.Models;
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

    private bool asc;
    private int i;
    private int page;
    private NCRadio Radio;
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

            Radio = MapToNCRadio(RadioChannel);
        }

        if (e.Parameter is NCRadio radio)
        {
            Radio = radio;
            RadioChannel = MapToNeteaseRadioChannel(radio);
        }

        TextBoxRadioName.Text = Radio.Name;
        TextBoxDJ.Content = Radio.DJ.Name;
        TextBlockDesc.Text = Radio.Description;
        if (_setting.noImage)
        {
            ImageRect.ImageSource = null;
        }
        else
        {
            var img = new BitmapImage();
            ImageRect.ImageSource = img;
            img.UriSource = new Uri(Radio.Cover + "?param=" + StaticSource.PICSIZE_SONGLIST_DETAIL_COVER);
        }

        Songs.Clear();
        _ascendingPrograms = null;
        SongContainer.QueueScope = SongListQueueScope.Radio(Radio.Id);
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
        var playlist = Ioc.Default.GetRequiredService<IPlaylistService>();
        await _navigator.AppendAsync(new MusicResource.Radio(Radio.Id));
        if (asc) playlist.ReverseList();
        await playlist.MoveToIndexAsync(0);
    }

    private void TextBoxDJ_OnTapped(object sender, RoutedEventArgs routedEventArgs)
    {
        _navigation.Navigate(typeof(Me), Radio.DJ.Id);
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
        var playlist = Ioc.Default.GetRequiredService<IPlaylistService>();
        await playlist.AppendRadioListAsync(Radio.Id, asc);
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

    private static NCRadio MapToNCRadio(NeteaseRadioChannel channel)
    {
        return new NCRadio
        {
            Cover = channel.CoverUrl,
            Description = channel.Description,
            DJ = MapToNCUser(channel.Host),
            Id = channel.ActualId,
            LastProgramName = channel.LastProgramName,
            Name = channel.Name,
            HasSubscribed = channel.Subscribed,
        };
    }

    private static NeteaseRadioChannel MapToNeteaseRadioChannel(NCRadio radio)
    {
        return new NeteaseRadioChannel
        {
            ActualId = radio.Id,
            Name = radio.Name,
            CoverUrl = radio.Cover,
            Description = radio.Description,
            Host = radio.DJ is null ? null : new NeteaseUser
            {
                ActualId = radio.DJ.Id,
                Name = radio.DJ.Name,
                AvatarUrl = radio.DJ.Avatar,
                Description = radio.DJ.Signature
            },
            LastProgramName = radio.LastProgramName,
            Subscribed = radio.HasSubscribed,
            CreatorList = radio.DJ is null ? [] : [radio.DJ.Name]
        };
    }

    private static NCUser MapToNCUser(NeteaseUser? user)
    {
        return new NCUser
        {
            Id = user?.ActualId,
            Name = user?.Name,
            Avatar = user?.AvatarUrl,
            Signature = user?.Description
        };
    }

}
