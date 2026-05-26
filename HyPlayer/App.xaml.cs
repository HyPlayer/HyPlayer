#region

using Depository.Abstraction.Enums;
using Depository.Abstraction.Interfaces;
using Depository.Abstraction.Interfaces.NotificationHub;
using Depository.Extensions;
using HyPlayer.Classes;
using HyPlayer.Domain;
using HyPlayer.Domain.Music;
using HyPlayer.Domain.Settings;
using HyPlayer.Features.Widgets.Services;
using HyPlayer.Infrastructure.Platform;
using HyPlayer.NeteaseApi;
using HyPlayer.NeteaseProvider;
using HyPlayer.NeteaseProvider.Models;
using HyPlayer.PlayCore;
using HyPlayer.PlayCore.Abstraction;
using HyPlayer.PlayCore.Abstraction.Interfaces.AudioServices;
using HyPlayer.PlayCore.Abstraction.Interfaces.Provider;
using HyPlayer.PlayCore.Abstraction.Models.Notifications;
using HyPlayer.PlayCore.PlayListControllers;
using HyPlayer.Services.Abstractions;
using HyPlayer.Services.AppState;
using HyPlayer.Services.Authentication;
using HyPlayer.Services.Background;
using HyPlayer.Services.Cache;
using HyPlayer.Services.Diagnostics;
using HyPlayer.Services.History;
using HyPlayer.Services.LastFM;
using HyPlayer.Services.Lyrics;
using HyPlayer.Services.Navigation;
using HyPlayer.Services.Notifications;
using HyPlayer.Services.Playback;
using HyPlayer.Services.Playback.LocalProvider;
using HyPlayer.Services.Playback.PlayCoreBridge;
using HyPlayer.Services.Playback.PlaylistService;
using HyPlayer.Services.Playback.QueueProviders;
using HyPlayer.Services.Playback.Strategies;
using HyPlayer.Services.Playback.Transitions;
using HyPlayer.Services.Runtime;
using HyPlayer.Services.Tiles;
using HyPlayer.Shell.Login;
using HyPlayer.Shell.Playback;
using HyPlayer.Shell.Search;
using HyPlayer.Shell.Services;
using HyPlayer.UI.Lists;
using HyPlayer.UI.Playback.PlayBar;
using HyPlayer.UI.TeachingTips;
using HyPlayer.UWP.Chopin.Abstractions.Interfaces;
using HyPlayer.UWP.Chopin.Abstractions.Models;
using Kawazu;
using LiteFM;
using LiteFM.Abstractions;
using Microsoft.Gaming.XboxGameBar;
using System;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Windows.ApplicationModel;
using Windows.ApplicationModel.Activation;
using Windows.Storage;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;
using WinRT;
using AlbumPageViewModel = HyPlayer.Features.Album.AlbumPageViewModel;
using ArtistPageViewModel = HyPlayer.Features.Artist.ArtistPageViewModel;
using ExpandedPlayerViewModel = HyPlayer.Shell.ExpandedPlayer.ExpandedPlayerViewModel;
using FavoriteViewModel = HyPlayer.Features.Library.FavoriteViewModel;
using HomeViewModel = HyPlayer.Features.Home.HomeViewModel;
using MeViewModel = HyPlayer.Features.User.MeViewModel;
using NavigationShellViewModel = HyPlayer.Shell.Navigation.NavigationShellViewModel;
using PlayBarViewModel = HyPlayer.UI.Playback.PlayBar.PlayBarViewModel;
using SongListViewModel = HyPlayer.Features.Playlist.SongListViewModel;
using WidgetPage = HyPlayer.Features.Widgets.WidgetPage;
using WidgetSettingsPage = HyPlayer.Features.Widgets.WidgetSettingsPage;

#endregion

namespace HyPlayer;

/// <summary>
///     提供特定于应用程序的行为，以补充默认的应用程序类。
/// </summary>
public sealed partial class App : Application
{
    /// <summary>
    ///     初始化单一实例应用程序对象。这是执行的创作代码的第一行，
    ///     已执行，逻辑上等同于 main() 或 WinMain()。
    /// </summary>
    private Frame rootFrame;

    public App()
    {
        InitializeComponent();
        InitializeServices();
        InitializeCommonServices();
        HistoryManagement.InitializeHistoryTrack();
        Suspending += OnSuspending;
        UnhandledException += App_UnhandledException;
        EnteredBackground += App_EnteredBackground;
        LeavingBackground += App_LeavingBackground;
        if (AppDepository.Resolve<Setting>().themeRequest != ThemeRequest.Auto)
            RequestedTheme = AppDepository.Resolve<Setting>().themeRequest == ThemeRequest.Light ? ApplicationTheme.Light : ApplicationTheme.Dark;
        AppDepository.Resolve<IBackgroundTaskRunner>().Forget(InitializeThings, "initialize app cache and converters");
    }

    private static void InitializeServices()
    {
        AppDepository.Initialize();
        InitializeServices(AppDepository.Root);
    }

    private static void InitializeCommonServices()
    {
        var setting = AppDepository.Resolve<Setting>();
        var neteaseProvider = AppDepository.Resolve<global::HyPlayer.NeteaseProvider.NeteaseProvider>();
        neteaseProvider.ConfigureAdditionalParameters(setting.ApiAdditionalParameters);
        neteaseProvider.ConfigureFakeCheckToken(setting.EnableCheckTokenApi);
        var globalTimer = AppDepository.Resolve<IGlobalTimerService>();
        var teachingTip = AppDepository.Resolve<ITeachingTipService>();
        var playBarAutoHide = AppDepository.Resolve<IPlayBarAutoHideService>();
        globalTimer.SecondTick += (_, _) =>
        {
            teachingTip.Roll();
            playBarAutoHide.Tick();
        };
    }

    private static void InitializeServices(IDepository depository)
    {
        var setting = new Setting();
        var neteaseProvider = new global::HyPlayer.NeteaseProvider.NeteaseProvider();
        var client = neteaseProvider.ConfigureHttpClient(setting.EnableProxy);
        depository.AddSingleton<HttpClient>(client);
        depository.AddSingleton<global::HyPlayer.NeteaseProvider.NeteaseProvider>(neteaseProvider);
        depository.AddSingleton<ProviderBase>(neteaseProvider);
        depository.AddSingleton<IContainerManagementProvidable>(neteaseProvider);
        depository.AddSingleton<IAuthenticationProvidable>(neteaseProvider);
        depository.AddSingleton<IQrAuthenticationProvidable>(neteaseProvider);
        depository.AddSingleton<ISearchSuggestionProvidable>(neteaseProvider);
        depository.AddSingleton<ICommentProvidable>(neteaseProvider);
        depository.AddSingleton<IProvidableItemCommentProvidable>(neteaseProvider);
        depository.AddSingleton<ILyricProvidable>(neteaseProvider);
        depository.AddSingleton<IMusicResourceProvidable>(neteaseProvider);
        depository.AddSingleton<IProvidableItemProvidable>(neteaseProvider);
        depository.AddSingleton<IProvidableItemRangeProvidable>(neteaseProvider);
        depository.AddSingleton<ISearchableProvider>(neteaseProvider);
        depository.AddSingleton<IContainerPageProvidable>(neteaseProvider);
        depository.AddSingleton<IProvidableItemDynamicMetadataProvidable>(neteaseProvider);
        depository.AddSingleton<NeteasePersonalFMContainer>(new NeteasePersonalFMContainer { ActualId = "default", Name = "私人 FM" });
        var localProvider = new LocalProvider();
        depository.AddSingleton<LocalProvider>(localProvider);
        depository.AddSingleton<ProviderBase>(localProvider);
        depository.AddSingleton<IMusicResourceProvidable>(localProvider);
        depository.AddSingleton<LastFMClient>(new LastFMClient(new LastFMOptions() { ApiKey = LastFMConstants.APIKEY, ApiSecret = LastFMConstants.SECRET }, client));
        depository.AddSingleton<Setting>(setting);
        depository.AddSingleton<AudioGraphPlayer>();
        depository.Add(typeof(IPlayer), typeof(AudioGraphPlayer), DependencyLifetime.Singleton, implementationFactory: dep => dep.Resolve<AudioGraphPlayer>());

        // ── PlayCore foundation (side-by-side; existing playback remains active) ──
        depository.AddSingleton<INotificationHub, PlayCoreNotificationHub>();
        depository.AddSingleton<DefaultPlayListManager>();
        depository.Add(typeof(PlayListManagerBase), typeof(DefaultPlayListManager), DependencyLifetime.Singleton, implementationFactory: dep => dep.Resolve<DefaultPlayListManager>());
        depository.AddSingleton<OrderedRollPlayController>();
        depository.Add(typeof(PlayControllerBase), typeof(OrderedRollPlayController), DependencyLifetime.Singleton, implementationFactory: dep => dep.Resolve<OrderedRollPlayController>());
        depository.Add(typeof(INotificationSubscriber<InnerPlayListChangedNotification>), typeof(OrderedRollPlayController), DependencyLifetime.Singleton, implementationFactory: dep => dep.Resolve<OrderedRollPlayController>());
        depository.AddSingleton<Chopin>();
        depository.Add(typeof(PlayCoreBase), typeof(Chopin), DependencyLifetime.Singleton, implementationFactory: dep => dep.Resolve<Chopin>());
        depository.Add(typeof(INotificationSubscriber<CurrentSongChangedNotification>), typeof(Chopin), DependencyLifetime.Singleton, implementationFactory: dep => dep.Resolve<Chopin>());
        depository.AddSingleton<ChopinAudioServiceAdapter>();
        depository.Add(typeof(AudioServiceBase), typeof(ChopinAudioServiceAdapter), DependencyLifetime.Singleton, implementationFactory: dep => dep.Resolve<ChopinAudioServiceAdapter>());
        depository.Add(typeof(IPlayAudioTicketService), typeof(ChopinAudioServiceAdapter), DependencyLifetime.Singleton, implementationFactory: dep => dep.Resolve<ChopinAudioServiceAdapter>());
        depository.Add(typeof(IPauseAudioTicketService), typeof(ChopinAudioServiceAdapter), DependencyLifetime.Singleton, implementationFactory: dep => dep.Resolve<ChopinAudioServiceAdapter>());
        depository.Add(typeof(IStopAudioTicketService), typeof(ChopinAudioServiceAdapter), DependencyLifetime.Singleton, implementationFactory: dep => dep.Resolve<ChopinAudioServiceAdapter>());
        depository.Add(typeof(IAudioTicketSeekableService), typeof(ChopinAudioServiceAdapter), DependencyLifetime.Singleton, implementationFactory: dep => dep.Resolve<ChopinAudioServiceAdapter>());
        depository.Add(typeof(IOutgoingVolumeChangeable), typeof(ChopinAudioServiceAdapter), DependencyLifetime.Singleton, implementationFactory: dep => dep.Resolve<ChopinAudioServiceAdapter>());
        depository.Add(typeof(IAudioTicketVolumeChangeable), typeof(ChopinAudioServiceAdapter), DependencyLifetime.Singleton, implementationFactory: dep => dep.Resolve<ChopinAudioServiceAdapter>());
        depository.Add(typeof(IPlaybackSpeedChangeable.IPlaybackRateChangeableService), typeof(ChopinAudioServiceAdapter), DependencyLifetime.Singleton, implementationFactory: dep => dep.Resolve<ChopinAudioServiceAdapter>());
        depository.Add(typeof(IAudioTicketListProvidable), typeof(ChopinAudioServiceAdapter), DependencyLifetime.Singleton, implementationFactory: dep => dep.Resolve<ChopinAudioServiceAdapter>());

        // ── 播放核心：状态中心 ──
        depository.AddSingleton<PlaybackStateService>();

        // ── 播放核心：播放策略 ──
        depository.AddSingleton<IPlayStrategy, SequentialStrategy>();       // seq — 列表循环
        depository.AddSingleton<IPlayStrategy, SingleRepeatStrategy>();     // sgl — 单曲循环
        depository.AddSingleton<IPlayStrategy, ShuffleNoRepeatStrategy>();  // shn — 随机不重复
        depository.AddSingleton<IPlayStrategy, PersonalFmStrategy>();       // pfm — 私人 FM
        depository.AddSingleton<IPlayStrategy, ListenTogetherStrategy>();   // ltg — 一起听

        // ── 播放核心：曲目过渡策略 ──
        depository.AddSingleton<ITrackTransition, DirectTransition>();      // dir — 直接切歌
        depository.AddSingleton<ITrackTransition, CrossFadeTransition>();   // xfd — 交叉淡入淡出
        depository.AddSingleton<ITrackTransition, GaplessTransition>();     // gap — 无缝衔接

        // ── 播放核心：队列源 Provider ──
        depository.AddSingleton<IQueueSourceProvider, PlaylistQueueSourceProvider>();
        depository.AddSingleton<IQueueSourceProvider, AlbumQueueSourceProvider>();
        depository.AddSingleton<IQueueSourceProvider, RadioQueueSourceProvider>();
        depository.AddSingleton<IQueueSourceProvider, SingerHotQueueSourceProvider>();
        depository.AddSingleton<IQueueSourceProvider, SingleSongQueueSourceProvider>();

        // ── 播放核心：服务 ──
        depository.AddSingleton<IBackgroundTaskRunner, BackgroundTaskRunner>();
        depository.AddSingleton<ILocalFileImportService, LocalFileImportService>();
        depository.AddSingleton<IPlaybackControlService, PlaybackControlService>();
        depository.AddSingleton<IPlaylistService, PlaylistService>();
        depository.AddSingleton<ISongListQueueBuilder, SongListQueueBuilder>();
        depository.AddSingleton<ILyricService, LyricService>();
        depository.AddSingleton<IPlaybackNotificationService, PlaybackNotificationService>();
        depository.AddSingleton<ITileService, TileService>();

        // ── 应用核心：认证 / 导航 / 通知 / UI 状态 ──
        depository.AddSingleton<IAuthService, AuthService>();
        depository.AddSingleton<INavigationService, NavigationService>();
        depository.AddSingleton<IAppNavigator, AppNavigator>();
        depository.AddSingleton<NavigationShellViewModel>();
        depository.AddSingleton<INotificationService, NotificationService>();
        depository.AddSingleton<IAppLifecycleStateService, AppLifecycleStateService>();
        depository.AddSingleton<IDisplayKeepAwakeService, DisplayKeepAwakeService>();
        depository.AddSingleton<IKawazuStateService, KawazuStateService>();
        depository.AddSingleton<IDiagnosticsStateService, DiagnosticsStateService>();
        depository.AddSingleton<IGameBarWidgetService, GameBarWidgetService>();
        depository.AddSingleton<IShellHostStateService, ShellHostStateService>();
        depository.AddSingleton<IGlobalTimerService, GlobalTimerService>();
        depository.AddSingleton<ITeachingTipService, TeachingTipService>();
        depository.AddSingleton<IPlayBarAutoHideService, PlayBarAutoHideService>();
        depository.AddSingleton<IPlaylistCollectionChangeNotifier, PlaylistCollectionChangeNotifier>();

        // ── 播放 UI：状态存储 / shell 状态机 / 表面协调器 ──
        depository.AddSingleton<PlaybackSurfaceStore>();
        depository.AddSingleton<PlaybackShellStateMachine>();
        depository.AddSingleton<IPlaybackSurfaceCoordinator, PlaybackSurfaceCoordinator>();
        depository.AddTransient<ShellSearchViewModel>();
        depository.AddSingleton<ShellLoginService>();

        // ── ViewModels ──
        depository.AddTransient<HomeViewModel>();
        depository.AddTransient<MeViewModel>();
        depository.AddTransient<ExpandedPlayerViewModel>();
        depository.AddTransient<ArtistPageViewModel>();
        depository.AddTransient<SongListViewModel>();
        depository.AddTransient<FavoriteViewModel>();
        depository.AddTransient<AlbumPageViewModel>();
        depository.AddTransient<PlayBarViewModel>();
        depository.AddTransient<GroupedSongsListViewModel>();
    }

    private static async Task InitializeThings()
    {
        try
        {
            await SimpleCacher.InitializeAsync();
            var sf = await ApplicationData.Current.LocalFolder.TryGetItemAsync("Romaji");
            if (sf != null) AppDepository.Resolve<IKawazuStateService>().Converter = new KawazuConverter(sf.Path);
        }
        catch
        {
            // ignored
        }

        if (AppDepository.Resolve<PlaybackSurfaceStore>().IsExpanded)
            AppDepository.Resolve<IBackgroundTaskRunner>().Forget(
                AppDepository.Resolve<INotificationService>().InvokeOnUIThread(() =>
                {
                    AppDepository.Resolve<IPlaybackSurfaceCoordinator>().RestoreExpandedSurface();
                }),
                "navigate expanded player during app initialization");
    }

    private void App_LeavingBackground(object sender, LeavingBackgroundEventArgs e)
    {
        var lifecycle = AppDepository.Resolve<IAppLifecycleStateService>();
        if (lifecycle.IsInBackground)
        {
            lifecycle.IsInBackground = false;
            lifecycle.NotifyEnteredForeground();
        }
    }

    private void App_EnteredBackground(object sender, EnteredBackgroundEventArgs e)
    {
        AppDepository.Resolve<IAppLifecycleStateService>().IsInBackground = true;
    }

    protected override void OnActivated(IActivatedEventArgs args)
    {
        XboxGameBarWidgetActivatedEventArgs widgetArgs = null;
        if (args.Kind == ActivationKind.Protocol)
        {
            var protocolArgs = args?.As<IProtocolActivatedEventArgs>();
            string scheme = protocolArgs.Uri.Scheme;
            if (scheme.Equals("ms-gamebarwidget"))
            {
                widgetArgs = args.As<XboxGameBarWidgetActivatedEventArgs>();
            }
        }
        if (widgetArgs != null)
        {
            if (widgetArgs.IsLaunchActivation)
            {
                var widgetFrame = new Frame();
                rootFrame = widgetFrame;
                widgetFrame.NavigationFailed += OnNavigationFailed;
                Window.Current.Content = widgetFrame;
                // Create Game Bar widget object which bootstraps the connection with Game Bar


                if (widgetArgs.AppExtensionId == "SettingWidget")
                {
                    var settingsWidget = new XboxGameBarWidget(
                        widgetArgs,
                        Window.Current.CoreWindow,
                        widgetFrame);
                    widgetFrame.Navigate(typeof(WidgetSettingsPage), settingsWidget);
                }
                else
                {
                    var gameBarWidget = new XboxGameBarWidget(
                        widgetArgs,
                        Window.Current.CoreWindow,
                        widgetFrame);
                    AppDepository.Resolve<IGameBarWidgetService>().Widget = gameBarWidget;
                    widgetFrame.Navigate(typeof(WidgetPage), gameBarWidget);

                }
                OnLaunchedOrActivatedAsync(args);
                Window.Current.Activate();
            }
            else
            {
                // You can perform whatever behavior you need based on the URI payload.
            }
        }

        base.OnActivated(args);
        if (args.Kind == ActivationKind.ToastNotification)
        {
            rootFrame = Window.Current.Content?.As<Frame>();
            if (rootFrame == null)
            {
                rootFrame = new Frame();
                Window.Current.Content = rootFrame;
            }

            rootFrame.Navigate(typeof(MainPage));
            Window.Current.Activate();
            if (AppDepository.Resolve<IPlaybackSurfaceCoordinator>().IsExpanded) return;
            var setting = AppDepository.Resolve<Setting>();
            var animation = setting.expandAnimation;
            try
            {
                setting.expandAnimation = false;
                AppDepository.Resolve<IPlaybackSurfaceCoordinator>().Expand();
            }
            finally
            {
                setting.expandAnimation = animation;
            }
        }
        if (args.Kind == ActivationKind.Protocol)
        {
            var launchUri = (args?.As<IProtocolActivatedEventArgs>())?.Uri;
            if (launchUri?.Host == "link.last.fm")
                AppDepository.Resolve<IBackgroundTaskRunner>().Forget(
                    LastFMManager.TryLoginLastfmAccountFromBrowser(launchUri.Query.Replace("?token=", string.Empty)),
                    "complete Last.FM browser login");
        }
    }

    private void App_UnhandledException(object sender, Windows.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        AppDepository.Resolve<IDiagnosticsStateService>().ErrorMessages.Add(e.Exception.ToString());
        e.Handled = true;
    }

    protected override void OnFileActivated(FileActivatedEventArgs args) => OnLaunchedOrActivatedAsync(args);

    protected override void OnLaunched(LaunchActivatedEventArgs args) => OnLaunchedOrActivatedAsync(args);

    private async void OnLaunchedOrActivatedAsync(IActivatedEventArgs args)
    {
        base.OnActivated(args);

        rootFrame = Window.Current.Content?.As<Frame>();
        if (rootFrame == null)
        {
            rootFrame = new Frame();
            rootFrame.NavigationFailed += OnNavigationFailed;

            if (args.PreviousExecutionState == ApplicationExecutionState.Terminated)
            {

            }

            Window.Current.Content = rootFrame;
        }

        // 直接启动
        if (args is LaunchActivatedEventArgs)
        {
            if (rootFrame.Content == null)
            {
                NavigateToRootPage(args);
                Window.Current.Activate();
            }

        }
        // 本地播放
        else if (args is FileActivatedEventArgs)
        {
            var playlist = AppDepository.Resolve<IPlaylistService>();
            var localFileImport = AppDepository.Resolve<ILocalFileImportService>();
            playlist.PlaySourceId = "local";
            ApplicationData.Current.LocalSettings.Values["curPlayingListHistory"] = "[]";

            NavigateToRootPage();
            Window.Current.Activate();
            var control = AppDepository.Resolve<IPlaybackControlService>();
            var player = AppDepository.Resolve<AudioGraphPlayer>();
            if (!player.PlayerCreated)
            {
                AppDepository.Resolve<IBackgroundTaskRunner>().Forget(control.InitializeAsync(), "initialize player for file activation");
            }
            foreach (var storageItem in (args?.As<FileActivatedEventArgs>()).Files)
            {
                var file = (StorageFile)storageItem;
                await localFileImport.RegisterFutureAccessAsync(file);
                var item = await localFileImport.LoadStorageFileAsync(file);
                playlist.AppendLocalItem(item);
            }

            playlist.PlaySourceId = "local";
            playlist.NotifyAppendDone();
            if (playlist.QueueCount > 0)
                await playlist.MoveToIndexAsync(0);
        }


    }

    private void NavigateToRootPage(IActivatedEventArgs args = null)
    {
        rootFrame.Navigate(typeof(MainPage), (args?.As<LaunchActivatedEventArgs>())?.Arguments);
    }

    /// <summary>
    ///     导航到特定页失败时调用
    /// </summary>
    /// <param name="sender">导航失败的框架</param>
    /// <param name="e">有关导航失败的详细信息</param>
    private void OnNavigationFailed(object sender, NavigationFailedEventArgs e)
    {
        throw new Exception("Failed to load Page " + e.SourcePageType.FullName);
    }

    /// <summary>
    ///     在将要挂起应用程序执行时调用。  在不知道应用程序
    ///     无需知道应用程序会被终止还是会恢复，
    ///     并让内存内容保持不变。
    /// </summary>
    /// <param name="sender">挂起的请求的源。</param>
    /// <param name="e">有关挂起请求的详细信息。</param>
    private async void OnSuspending(object sender, SuspendingEventArgs e)
    {
        var deferral = e.SuspendingOperation.GetDeferral();
        var playlist = AppDepository.Resolve<IPlaylistService>();
        var neteaseItems = playlist.ProviderQueueSnapshot
            .Where(t => t is not null && t.ProviderId == "ncm")
            .Select(t => t!)
            .ToList();
        var currentItem = playlist.NowPlayingProviderItem;
        var currentIndex = currentItem?.ProviderId == "ncm"
            ? neteaseItems.FindIndex(item => item.TypeId == currentItem.TypeId && item.ActualId == currentItem.ActualId)
            : -1;
        await HistoryManagement.SetcurPlayingListHistory([.. neteaseItems.Select(t => t.ActualId).Where(id => !string.IsNullOrWhiteSpace(id))!], currentIndex);
        AppDepository.Resolve<IGameBarWidgetService>().Widget?.Close();
        deferral.Complete();
    }
}
