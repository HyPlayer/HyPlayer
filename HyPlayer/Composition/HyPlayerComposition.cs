using System.Net.Http;
using Depository.Abstraction.Enums;
using Depository.Abstraction.Interfaces;
using Depository.Abstraction.Interfaces.NotificationHub;
using Depository.Extensions;
using HyPlayer.Application.Diagnostics;
using HyPlayer.Application.Notifications;
using HyPlayer.Application.State;
using HyPlayer.Application.Threading;
using HyPlayer.Classes;
using HyPlayer.Domain.Settings;
using HyPlayer.Features.Account.Services;
using HyPlayer.Features.Downloads.Services;
using HyPlayer.Features.History.Services;
using HyPlayer.Features.LastFM.Services;
using HyPlayer.Features.Lyrics.Effects;
using HyPlayer.Features.Lyrics.Services;
using HyPlayer.Features.Playback.QueueProviders;
using HyPlayer.Features.Playback.Services;
using HyPlayer.Features.Playback.Transitions;
using HyPlayer.Features.Widgets.Services;
using HyPlayer.LyricEffects.Drawing;
using HyPlayer.LyricEffects.Expressions;
using HyPlayer.LyricRenderer.Pipeline;
using HyPlayer.Platform.Playback.AudioServices;
using HyPlayer.Platform.Playback.LocalProvider;
using HyPlayer.Platform.Runtime;
using HyPlayer.Platform.Runtime.Background;
using HyPlayer.Platform.Storage;
using HyPlayer.Platform.SystemServices;
using HyPlayer.Platform.Tiles;
using HyPlayer.Platform.Xaml;
using HyPlayer.PlayCore;
using HyPlayer.PlayCore.Abstraction;
using HyPlayer.PlayCore.Abstraction.Interfaces.AudioServices;
using HyPlayer.PlayCore.Abstraction.Interfaces.Provider;
using HyPlayer.PlayCore.Abstraction.Models.Notifications;
using HyPlayer.PlayCore.PlayListControllers;
using HyPlayer.Shell.ExpandedPlayer;
using HyPlayer.Shell.Login;
using HyPlayer.Shell.Navigation;
using HyPlayer.Shell.Navigation.Services;
using HyPlayer.Shell.Playback;
using HyPlayer.Shell.Search;
using HyPlayer.Shell.Services;
using HyPlayer.UI.Playback.PlayBar;
using HyPlayer.UI.TeachingTips;
using HyPlayer.UWP.Chopin.Abstractions.Interfaces;
using HyPlayer.UWP.Chopin.Abstractions.Models;
using LiteFM;
using LiteFM.Abstractions;
using AlbumPageViewModel = HyPlayer.Features.Album.AlbumPageViewModel;
using ArtistPageViewModel = HyPlayer.Features.Artist.ArtistPageViewModel;
using FavoriteViewModel = HyPlayer.Features.Library.FavoriteViewModel;
using HomeViewModel = HyPlayer.Features.Home.HomeViewModel;
using MeViewModel = HyPlayer.Features.User.MeViewModel;
using SongListViewModel = HyPlayer.Features.Playlist.SongListViewModel;

namespace HyPlayer.Composition;

internal static class HyPlayerComposition
{
    public static void ConfigureServices(IDepository depository)
    {
        var playbackSettings = new PlaybackSettings();
        var uiSettings = new UISettings();
        var apiSettings = new ApiSettings();
        var lyricSettings = new LyricSettings();
        var lastFmSettings = new LastFMSettings();
        var downloadSettings = new DownloadSettings();
        var localLibrarySettings = new LocalLibrarySettings(downloadSettings);
        // The legacy settings page still exposes its bindings through XamlHelpers.Setting.
        // Keep the facade registered while the split settings services are used by new code.
        var legacySettings = new Setting();

        depository.AddSingleton<PlaybackSettings>(playbackSettings);
        depository.AddSingleton<UISettings>(uiSettings);
        depository.AddSingleton<ApiSettings>(apiSettings);
        depository.AddSingleton<LyricSettings>(lyricSettings);
        depository.AddSingleton<LastFMSettings>(lastFmSettings);
        depository.AddSingleton<DownloadSettings>(downloadSettings);
        depository.AddSingleton<LocalLibrarySettings>(localLibrarySettings);
        depository.AddSingleton<Setting>(legacySettings);

        var neteaseProvider = new NeteaseProvider.NeteaseProvider();
        var client = neteaseProvider.ConfigureHttpClient(apiSettings.EnableProxy);

        ConfigureProviders(depository, neteaseProvider, client);
        ConfigurePlayCore(depository);
        ConfigurePlaybackServices(depository);
        ConfigureApplicationServices(depository);
        ConfigureShellServices(depository);
        ConfigureViewModels(depository);
    }

    private static void ConfigureProviders(
        IDepository depository,
        NeteaseProvider.NeteaseProvider neteaseProvider,
        HttpClient client)
    {
        depository.AddSingleton<HttpClient>(client);
        depository.AddSingleton<NeteaseProvider.NeteaseProvider>(neteaseProvider);
        depository.AddSingleton<ProviderBase>(neteaseProvider);
        depository.AddSingleton<IContainerManagementProvidable>(neteaseProvider);
        depository.AddSingleton<IAuthenticationProvidable>(neteaseProvider);
        depository.AddSingleton<IQrAuthenticationProvidable>(neteaseProvider);
        depository.AddSingleton<ISearchSuggestionProvidable>(neteaseProvider);
        depository.AddSingleton<ICommentProvidable>(neteaseProvider);
        depository.AddSingleton<IProvidableItemCommentProvidable>(neteaseProvider);
        depository.AddSingleton<IProvableItemLikable>(neteaseProvider);
        depository.AddSingleton<ILyricProvidable>(neteaseProvider);
        depository.AddSingleton<IMusicResourceProvidable>(neteaseProvider);
        depository.AddSingleton<IProvidableItemProvidable>(neteaseProvider);
        depository.AddSingleton<IProvidableItemRangeProvidable>(neteaseProvider);
        depository.AddSingleton<ISearchableProvider>(neteaseProvider);
        depository.AddSingleton<IContainerPageProvidable>(neteaseProvider);
        depository.AddSingleton<IProviderKnownTypeIds>(neteaseProvider);
        depository.AddSingleton<IPersonalRadioProvidable>(neteaseProvider);
        depository.AddSingleton<IProvidableItemDynamicMetadataProvidable>(neteaseProvider);
        depository.AddSingleton<IProviderNetworkConfigurationProvidable>(neteaseProvider);
        depository.AddSingleton<IContainerItemManagementProvidable>(neteaseProvider);
        depository.AddSingleton<IProviderAdditionalConfigurationProvidable>(neteaseProvider);
        depository.AddSingleton<IProviderSearchCategoryTypeIds>(neteaseProvider);
        depository.AddSingleton<IUserLibraryTypeIds>(neteaseProvider);
        depository.AddSingleton<IUserLibraryProvidable>(neteaseProvider);
        depository.AddSingleton<IUserLibraryNavigationProvidable>(neteaseProvider);
        depository.AddSingleton<IProviderSpecialContainerTypeIds>(neteaseProvider);
        depository.AddSingleton<IResourceQualityTagProvidable>(neteaseProvider);

        var localProvider = new LocalProvider();
        depository.AddSingleton<LocalProvider>(localProvider);
        depository.AddSingleton<ProviderBase>(localProvider);
        depository.AddSingleton<IMusicResourceProvidable>(localProvider);

        depository.AddSingleton<LastFMClient>(
            new LastFMClient(
                new LastFMOptions { ApiKey = LastFMConstants.ApiKey, ApiSecret = LastFMConstants.Secret },
                client));
        depository.AddSingleton<AudioGraphPlayer>();
        depository.Add(
            typeof(IPlayer),
            typeof(AudioGraphPlayer),
            DependencyLifetime.Singleton,
            implementationFactory: dep => dep.Resolve<AudioGraphPlayer>());
    }

    private static void ConfigurePlayCore(IDepository depository)
    {
        depository.AddSingleton<INotificationHub, PlayCoreNotificationHub>();
        depository.AddSingleton<DefaultPlayListManager>();
        depository.Add(
            typeof(PlayListManagerBase),
            typeof(DefaultPlayListManager),
            DependencyLifetime.Singleton,
            implementationFactory: dep => dep.Resolve<DefaultPlayListManager>());
        depository.AddSingleton<OrderedRollPlayController>();
        depository.Add(
            typeof(PlayControllerBase),
            typeof(OrderedRollPlayController),
            DependencyLifetime.Singleton,
            implementationFactory: dep => dep.Resolve<OrderedRollPlayController>());
        depository.Add(
            typeof(INotificationSubscriber<InnerPlayListChangedNotification>),
            typeof(OrderedRollPlayController),
            DependencyLifetime.Singleton,
            implementationFactory: dep => dep.Resolve<OrderedRollPlayController>());
        depository.AddSingleton<Chopin>();
        depository.Add(
            typeof(PlayCoreBase),
            typeof(Chopin),
            DependencyLifetime.Singleton,
            implementationFactory: dep => dep.Resolve<Chopin>());
        depository.Add(
            typeof(INotificationSubscriber<CurrentSongChangedNotification>),
            typeof(Chopin),
            DependencyLifetime.Singleton,
            implementationFactory: dep => dep.Resolve<Chopin>());
        depository.AddSingleton<ChopinAudioService>();
        depository.Add(
            typeof(AudioServiceBase),
            typeof(ChopinAudioService),
            DependencyLifetime.Singleton,
            implementationFactory: dep => dep.Resolve<ChopinAudioService>());
        depository.Add(
            typeof(IPlayAudioTicketService),
            typeof(ChopinAudioService),
            DependencyLifetime.Singleton,
            implementationFactory: dep => dep.Resolve<ChopinAudioService>());
        depository.Add(
            typeof(IPauseAudioTicketService),
            typeof(ChopinAudioService),
            DependencyLifetime.Singleton,
            implementationFactory: dep => dep.Resolve<ChopinAudioService>());
        depository.Add(
            typeof(IStopAudioTicketService),
            typeof(ChopinAudioService),
            DependencyLifetime.Singleton,
            implementationFactory: dep => dep.Resolve<ChopinAudioService>());
        depository.Add(
            typeof(IAudioTicketSeekableService),
            typeof(ChopinAudioService),
            DependencyLifetime.Singleton,
            implementationFactory: dep => dep.Resolve<ChopinAudioService>());
        depository.Add(
            typeof(IOutgoingVolumeChangeable),
            typeof(ChopinAudioService),
            DependencyLifetime.Singleton,
            implementationFactory: dep => dep.Resolve<ChopinAudioService>());
        depository.Add(
            typeof(IAudioTicketVolumeChangeable),
            typeof(ChopinAudioService),
            DependencyLifetime.Singleton,
            implementationFactory: dep => dep.Resolve<ChopinAudioService>());
        depository.Add(
            typeof(IPlaybackSpeedChangeable.IPlaybackRateChangeableService),
            typeof(ChopinAudioService),
            DependencyLifetime.Singleton,
            implementationFactory: dep => dep.Resolve<ChopinAudioService>());
        depository.Add(
            typeof(IPreparedAudioTicketService),
            typeof(ChopinAudioService),
            DependencyLifetime.Singleton,
            implementationFactory: dep => dep.Resolve<ChopinAudioService>());
        depository.Add(
            typeof(IAudioTicketListProvidable),
            typeof(ChopinAudioService),
            DependencyLifetime.Singleton,
            implementationFactory: dep => dep.Resolve<ChopinAudioService>());
    }

    private static void ConfigurePlaybackServices(IDepository depository)
    {
        depository.AddSingleton<PlaybackStateService>();
        depository.AddSingleton<IQueueSourceProvider, PlaylistQueueSourceProvider>();
        depository.AddSingleton<IQueueSourceProvider, AlbumQueueSourceProvider>();
        depository.AddSingleton<IQueueSourceProvider, RadioQueueSourceProvider>();
        depository.AddSingleton<IQueueSourceProvider, DailyRecommendQueueSourceProvider>();
        depository.AddSingleton<IQueueSourceProvider, SingerHotQueueSourceProvider>();
        depository.AddSingleton<IQueueSourceProvider, SingleSongQueueSourceProvider>();
        depository.AddSingleton<ILocalFileImportService, LocalFileImportService>();
        depository.AddSingleton<ITrackTransition, DirectTransition>();
        depository.AddSingleton<ITrackTransition, GaplessTransition>();
        depository.AddSingleton<ITrackTransition, CrossFadeTransition>();
        depository.AddSingleton<IPlaybackControlService, PlaybackControlService>();
        depository.Add(
            typeof(INotificationSubscriber<PlaybackRequestFailedNotification>),
            typeof(PlaybackControlService),
            DependencyLifetime.Singleton,
            implementationFactory: dep => dep.Resolve<IPlaybackControlService>());
        depository.AddSingleton<IPlaybackQueueLoader, PlaybackQueueLoader>();
        depository.AddSingleton<PlayCoreStateSynchronizer>();
        depository.Add(
            typeof(INotificationSubscriber<CurrentSongChangedNotification>),
            typeof(PlayCoreStateSynchronizer),
            DependencyLifetime.Singleton,
            implementationFactory: dep => dep.Resolve<PlayCoreStateSynchronizer>());
        depository.Add(
            typeof(INotificationSubscriber<OrderedPlaylistChangedNotification>),
            typeof(PlayCoreStateSynchronizer),
            DependencyLifetime.Singleton,
            implementationFactory: dep => dep.Resolve<PlayCoreStateSynchronizer>());
        depository.Add(
            typeof(INotificationSubscriber<InnerPlayListChangedNotification>),
            typeof(PlayCoreStateSynchronizer),
            DependencyLifetime.Singleton,
            implementationFactory: dep => dep.Resolve<PlayCoreStateSynchronizer>());
        depository.AddSingleton<ISongListQueueBuilder, SongListQueueBuilder>();
        depository.AddSingleton<ILyricService, LyricService>();
        depository.AddSingleton<IPlaybackNotificationService, PlaybackNotificationService>();
        depository.AddSingleton<IPlaybackMemoryService, PlaybackMemoryService>();
    }

    private static void ConfigureApplicationServices(IDepository depository)
    {
        depository.AddSingleton<IBackgroundTaskRunner, BackgroundTaskRunner>();
        depository.AddSingleton<IAuthSessionStore, AuthSessionStore>();
        depository.AddSingleton<IAuthService, AuthService>();
        depository.AddSingleton<INavigationService, NavigationService>();
        depository.AddSingleton<IAppNavigator, AppNavigator>();
        depository.AddSingleton<IUIThreadDispatcher, UIThreadDispatcher>();
        depository.AddSingleton<INotificationService, NotificationService>();
        depository.AddSingleton<IAppLifecycleStateService, AppLifecycleStateService>();
        depository.AddSingleton<IUserLibraryStateService, UserLibraryStateService>();
        depository.AddSingleton<IHistoryService, HistoryService>();
        depository.AddSingleton<IDownloadService, DownloadService>();
        depository.AddSingleton<IDisplayKeepAwakeService, DisplayKeepAwakeService>();
        depository.AddSingleton<IKawazuStateService, KawazuStateService>();
        depository.AddSingleton<IDiagnosticsStateService, DiagnosticsStateService>();
        depository.AddSingleton<IGameBarWidgetService, GameBarWidgetService>();
        depository.AddSingleton<IGlobalTimerService, GlobalTimerService>();
        depository.AddSingleton<ITeachingTipService, TeachingTipService>();
        depository.AddSingleton<IPlayBarAutoHideService, PlayBarAutoHideService>();
        depository.AddSingleton<IPlaylistCollectionChangeNotifier, PlaylistCollectionChangeNotifier>();
        depository.AddSingleton<ILastFmService, LastFmService>();
        depository.AddSingleton<ITileService, TileService>();
        depository.AddSingleton<ILyricExpressionCompiler, LyricExpressionCompiler>();
        depository.AddSingleton<ILyricDrawScriptParser, LyricDrawScriptParser>();
        depository.AddSingleton<LyricDrawCommandRegistry>();
        depository.AddSingleton<ILyricRenderOperationRegistry, LyricRenderOperationRegistry>();
        depository.AddSingleton<ILyricEffectProfileService, LyricEffectProfileService>();
    }

    private static void ConfigureShellServices(IDepository depository)
    {
        depository.AddSingleton<NavigationShellViewModel>();
        depository.AddSingleton<IShellHostStateService, ShellHostStateService>();
        depository.AddSingleton<PlaybackSurfaceStore>();
        depository.AddSingleton<PlaybackShellStateMachine>();
        depository.AddSingleton<IPlaybackSurfaceCoordinator, PlaybackSurfaceCoordinator>();
        depository.AddTransient<ShellSearchViewModel>();
        depository.AddSingleton<ShellLoginService>();
    }

    private static void ConfigureViewModels(IDepository depository)
    {
        depository.AddTransient<HomeViewModel>();
        depository.AddTransient<MeViewModel>();
        depository.AddTransient<ExpandedPlayerViewModel>();
        depository.AddTransient<ArtistPageViewModel>();
        depository.AddTransient<SongListViewModel>();
        depository.AddTransient<FavoriteViewModel>();
        depository.AddTransient<AlbumPageViewModel>();
        depository.AddTransient<PlayBarViewModel>();
    }
}
