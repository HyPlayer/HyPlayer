using System;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;
using CommunityToolkit.Mvvm.DependencyInjection;
using HyPlayer.Application.Loading;
using HyPlayer.Application.Notifications;
using HyPlayer.Domain.Settings;
using HyPlayer.Features.Playback.QueueProviders;
using HyPlayer.Features.Playback.Services;
using HyPlayer.Features.Radio.Services;
using HyPlayer.PlayCore.Abstraction;
using HyPlayer.PlayCore.Abstraction.Models;
using HyPlayer.PlayCore.Abstraction.Models.SingleItems;

namespace HyPlayer.Features.Radio;

public sealed partial class PodcastEpisodesPage : Page
{
    public static readonly DependencyProperty EpisodesContainerProperty = DependencyProperty.Register(
        nameof(EpisodesContainer), typeof(ContainerBase), typeof(PodcastEpisodesPage),
        new PropertyMetadata(default(ContainerBase)));

    private readonly ApiSettings _apiSettings = Ioc.Default.GetRequiredService<ApiSettings>();
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private readonly IPlaybackControlService _control = Ioc.Default.GetRequiredService<IPlaybackControlService>();

    private readonly LoadState _load = new("单集", Ioc.Default.GetRequiredService<ILoadEnvironment>())
    {
        RequiresAccount = true
    };

    private readonly INotificationService _notification = Ioc.Default.GetRequiredService<INotificationService>();
    private readonly PlayCoreBase _playCore = Ioc.Default.GetRequiredService<PlayCoreBase>();

    private readonly IPodcastLibraryService _podcastLibrary =
        Ioc.Default.GetRequiredService<IPodcastLibraryService>();

    private bool _isPlayingAll;

    public PodcastEpisodesPage()
    {
        InitializeComponent();
    }

    public ContainerBase EpisodesContainer
    {
        get => (ContainerBase)GetValue(EpisodesContainerProperty);
        set => SetValue(EpisodesContainerProperty, value);
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        var container = _podcastLibrary.CreateEpisodesContainer(
            e.Parameter as string == PodcastEpisodeKinds.Collected
                ? PodcastEpisodeKinds.Collected
                : PodcastEpisodeKinds.Liked);
        TitleText.Text = container.Name;
        SubtitleText.Text = "正在加载…";
        SongContainer.State.PropertyChanged += ListState_PropertyChanged;
        _load.PropertyChanged += ListState_PropertyChanged;
        SongContainer.GreedyLoad = _apiSettings.GreedilyLoadPlayContainerItems;
        SongContainer.ContainerLoad = _load;
        _ = _load.RunAsync(
            _ => Task.FromResult(container),
            value => EpisodesContainer = value,
            static value => value is null);
    }

    private void ListState_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        var state = SongContainer.State;
        var count = state.Rows.Count;
        PlayAllButton.IsEnabled = count > 0 && !_isPlayingAll;
        if (_load.IsLoading || _load.IsFailed)
            SubtitleText.Text = _load.Title;
        else if (state.CanRetry)
            SubtitleText.Text = "加载失败";
        else if (state.IsLoading && count == 0)
            SubtitleText.Text = "正在加载…";
        else if (count == 0)
            SubtitleText.Text = "这里还没有单集";
        else
            SubtitleText.Text = state.HasMore ? $"已加载 {count} 个单集" : $"共 {count} 个单集";
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        _cancellationTokenSource.Cancel();
        _cancellationTokenSource.Dispose();
        SongContainer.State.PropertyChanged -= ListState_PropertyChanged;
        _load.PropertyChanged -= ListState_PropertyChanged;
        SongContainer.ReleaseResources();
        Bindings.StopTracking();
    }

    private async void PlayAll_Click(object sender, RoutedEventArgs e)
    {
        if (EpisodesContainer is not { } container) return;

        _isPlayingAll = true;
        PlayAllButton.IsEnabled = false;
        try
        {
            var episodes = (await ContainerItemLoader.LoadAllAsync(container, _cancellationTokenSource.Token))
                .OfType<SingleSongBase>()
                .ToList();
            if (episodes.Count == 0) return;

            await _control.StopAsync();
            await _control.ClearQueueAsync();
            await _playCore.InsertSongRangeAsync(episodes);
            _playCore.PlaySourceId = string.Empty;
            await _control.MoveNextAndPlayAsync(true);
        }
        catch (OperationCanceledException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
        catch (Exception ex)
        {
            _notification.ShowFailure("加载播客单集失败", ex);
        }
        finally
        {
            _isPlayingAll = false;
            PlayAllButton.IsEnabled = SongContainer.State.Rows.Count > 0;
        }
    }
}
