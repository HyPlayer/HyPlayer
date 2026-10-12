using System;
using System.Linq;
using System.Threading;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;
using CommunityToolkit.Mvvm.DependencyInjection;
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
    private readonly INotificationService _notification = Ioc.Default.GetRequiredService<INotificationService>();
    private readonly PlayCoreBase _playCore = Ioc.Default.GetRequiredService<PlayCoreBase>();

    private readonly IPodcastLibraryService _podcastLibrary =
        Ioc.Default.GetRequiredService<IPodcastLibraryService>();

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
        SongContainer.GreedyLoad = _apiSettings.GreedilyLoadPlayContainerItems;
        EpisodesContainer = container;
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        _cancellationTokenSource.Cancel();
        _cancellationTokenSource.Dispose();
        SongContainer.ReleaseResources();
        Bindings.StopTracking();
    }

    private async void PlayAll_Click(object sender, RoutedEventArgs e)
    {
        if (EpisodesContainer is not { } container) return;

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
            _notification.ShowMessage("加载播客单集失败", ex.Message);
        }
        finally
        {
            PlayAllButton.IsEnabled = true;
        }
    }
}
