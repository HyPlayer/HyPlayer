#region

using System;
using System.Threading.Tasks;
using Windows.ApplicationModel;
using Windows.ApplicationModel.Activation;
using Windows.Storage;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;
using CommunityToolkit.Mvvm.DependencyInjection;
using Depository.Extensions.DependencyInjection;
using HyPlayer.Application;
using HyPlayer.Application.Diagnostics;
using HyPlayer.Application.State;
using HyPlayer.Application.Threading;
using HyPlayer.Composition;
using HyPlayer.Domain;
using HyPlayer.Domain.Settings;
using HyPlayer.Features.History.Services;
using HyPlayer.Features.LastFM.Services;
using HyPlayer.Features.Lyrics.Effects;
using HyPlayer.Features.Lyrics.Services;
using HyPlayer.Features.Playback.DynamicCover;
using HyPlayer.Features.Playback.Services;
using HyPlayer.Features.Settings.Services;
using HyPlayer.Features.Widgets.Services;
using HyPlayer.Platform.Runtime;
using HyPlayer.Platform.Downloads;
using HyPlayer.Platform.Playback.LocalProvider;
using HyPlayer.Platform.Runtime.Background;
using HyPlayer.Platform.Storage;
using HyPlayer.Platform.Storage.Cache;
using HyPlayer.PlayCore.Abstraction;
using HyPlayer.Shell.Playback;
using HyPlayer.UI.Playback.PlayBar;
using HyPlayer.UI.TeachingTips;
using Kawazu;
using Microsoft.Gaming.XboxGameBar;
using WinRT;
using ControlsResourcesVersion = Microsoft.UI.Xaml.Controls.ControlsResourcesVersion;
using UnhandledExceptionEventArgs = Windows.UI.Xaml.UnhandledExceptionEventArgs;
using WidgetPage = HyPlayer.Features.Widgets.WidgetPage;
using WidgetSettingsPage = HyPlayer.Features.Widgets.WidgetSettingsPage;
using XamlControlsResources = Microsoft.UI.Xaml.Controls.XamlControlsResources;

#endregion

namespace HyPlayer;

/// <summary>
///     提供特定于应用程序的行为，以补充默认的应用程序类。
/// </summary>
public sealed partial class App : Windows.UI.Xaml.Application
{
    private bool _playbackMemoryRestoreRequested;
    private Task _maintenancePause = Task.CompletedTask;

    /// <summary>
    ///     初始化单一实例应用程序对象。这是执行的创作代码的第一行，
    ///     已执行，逻辑上等同于 main() 或 WinMain()。
    /// </summary>
    private Frame _rootFrame;

    public App()
    {
        Suspending += OnSuspending;
        Resuming += OnResuming;
        UnhandledException += App_UnhandledException;
        EnteredBackground += App_EnteredBackground;
        LeavingBackground += App_LeavingBackground;
        InitializeComponent();
        InitializeServices();
        InitializeCommonServices();
        AppDepository.Resolve<IHistoryService>().InitializeHistoryTrack();
        _ = AppDepository.Resolve<IPlaybackMemoryService>().InitializeAsync();
        DownloadedTrackIndex.ScheduleScan(TimeSpan.FromSeconds(40));
        StorageMaintenance.ScheduleTrim(TimeSpan.FromSeconds(30));
        var uiSettings = AppDepository.Resolve<UISettings>();
        if (uiSettings.XboxHidePointer)
        {
            RequiresPointerMode = ApplicationRequiresPointerMode.WhenRequested;
            FocusVisualKind = FocusVisualKind.Reveal;
        }

        if (uiSettings.IsOldThemeEnabled)
            foreach (var dictionary in Resources.MergedDictionaries)
                if (dictionary is XamlControlsResources controlsResources)
                    controlsResources.ControlsResourcesVersion = ControlsResourcesVersion.Version1;

        if (uiSettings.ThemeRequest != ThemeRequest.Auto)
            RequestedTheme = uiSettings.ThemeRequest == ThemeRequest.Light
                ? ApplicationTheme.Light
                : ApplicationTheme.Dark;
        AppDepository.Resolve<IBackgroundTaskRunner>().Forget(InitializeThings, "initialize app cache and converters");
    }

    private static void InitializeServices()
    {
        AppDepository.Initialize();
        HyPlayerComposition.ConfigureServices(AppDepository.Root);
        Ioc.Default.ConfigureServices(new DepositoryServiceProvider(AppDepository.Root));
    }

    private static void InitializeCommonServices()
    {
        var settings = AppDepository.Resolve<ApiSettings>();
        var neteaseProvider = AppDepository.Resolve<NeteaseProvider.NeteaseProvider>();
        neteaseProvider.ImportAdditionalConfiguration(settings.ApiAdditionalParametersJson);
        neteaseProvider.ConfigureFakeCheckToken(settings.EnableCheckTokenApi);
        var globalTimer = AppDepository.Resolve<IGlobalTimerService>();
        var teachingTip = AppDepository.Resolve<ITeachingTipService>();
        var playBarAutoHide = AppDepository.Resolve<IPlayBarAutoHideService>();
        AppDepository.Resolve<ToastLyricService>();
        globalTimer.SecondTick += (_, _) =>
        {
            teachingTip.Roll();
            playBarAutoHide.Tick();
        };
    }

    private static async Task InitializeThings()
    {
        AppDepository.Resolve<IBackgroundTaskRunner>().Forget(
            AppDepository.Resolve<IDynamicCoverService>().ClearCacheAsync(), "clear dynamic cover cache");
        AppDepository.Resolve<IBackgroundTaskRunner>().Forget(
            Task.Run(() => HyPlayer.Features.Settings.Settings.SystemFonts.Value), "enumerate system fonts");
        try
        {
            await SimpleCacher.InitializeAsync();
            await AppDepository.Resolve<ILyricRenderSettingsService>().InitializeAsync();
            var sf = await ApplicationData.Current.LocalFolder.TryGetItemAsync("Romaji");
            if (sf != null) AppDepository.Resolve<IKawazuStateService>().Converter = new KawazuConverter(sf.Path);
        }
        catch
        {
            // ignored
        }

        if (AppDepository.Resolve<PlaybackSurfaceStore>().IsExpanded)
            AppDepository.Resolve<IBackgroundTaskRunner>().Forget(
                AppDepository.Resolve<IUIThreadDispatcher>().TryRunAsync(() =>
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

        AppDepository.Resolve<IMemoryGovernor>().OnLeavingBackground();
    }

    private void App_EnteredBackground(object sender, EnteredBackgroundEventArgs e)
    {
        AppDepository.Resolve<IAppLifecycleStateService>().IsInBackground = true;
        AppDepository.Resolve<IMemoryGovernor>().OnEnteredBackground();
    }

    protected override void OnActivated(IActivatedEventArgs args)
    {
        XboxGameBarWidgetActivatedEventArgs widgetArgs = null;
        if (args.Kind == ActivationKind.Protocol)
        {
            var protocolArgs = args?.As<IProtocolActivatedEventArgs>();
            var scheme = protocolArgs.Uri.Scheme;
            if (scheme.Equals("ms-gamebarwidget")) widgetArgs = args.As<XboxGameBarWidgetActivatedEventArgs>();
        }

        if (widgetArgs != null)
            if (widgetArgs.IsLaunchActivation)
            {
                var widgetFrame = new Frame();
                _rootFrame = widgetFrame;
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

        // You can perform whatever behavior you need based on the URI payload.
        base.OnActivated(args);
        if (args.Kind == ActivationKind.ToastNotification)
        {
            _rootFrame = Window.Current.Content?.As<Frame>();
            if (_rootFrame == null)
            {
                _rootFrame = new Frame();
                Window.Current.Content = _rootFrame;
            }

            _rootFrame.Navigate(typeof(MainPage));
            Window.Current.Activate();
            if (AppDepository.Resolve<IPlaybackSurfaceCoordinator>().IsExpanded) return;
            var settings = AppDepository.Resolve<UISettings>();
            var animation = settings.ExpandAnimation;
            try
            {
                settings.ExpandAnimation = false;
                AppDepository.Resolve<IPlaybackSurfaceCoordinator>().Expand();
            }
            finally
            {
                settings.ExpandAnimation = animation;
            }
        }

        if (args.Kind == ActivationKind.Protocol)
        {
            var launchUri = args?.As<IProtocolActivatedEventArgs>()?.Uri;
            if (launchUri?.Host == "link.last.fm")
                AppDepository.Resolve<IBackgroundTaskRunner>().Forget(
                    AppDepository.Resolve<ILastFmService>()
                        .CompleteBrowserLoginAsync(launchUri.Query.Replace("?token=", string.Empty)),
                    "complete Last.FM browser login");
        }
    }

    private void App_UnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        AppDepository.Resolve<IDiagnosticsStateService>().AddError(e.Exception.ToString());
        e.Handled = true;
    }

    protected override void OnFileActivated(FileActivatedEventArgs args)
    {
        OnLaunchedOrActivatedAsync(args);
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        OnLaunchedOrActivatedAsync(args);
    }

    private async void OnLaunchedOrActivatedAsync(IActivatedEventArgs args)
    {
        base.OnActivated(args);

        _rootFrame = Window.Current.Content?.As<Frame>();
        if (_rootFrame == null)
        {
            _rootFrame = new Frame();
            _rootFrame.NavigationFailed += OnNavigationFailed;

            if (args.PreviousExecutionState == ApplicationExecutionState.Terminated)
            {
            }

            Window.Current.Content = _rootFrame;
            AppDepository.Resolve<HyPlayer.Platform.Xaml.AppearanceCustomizer>().ApplyFont();
        }

        AppDepository.Resolve<IMemoryGovernor>().Attach(Window.Current);
        ResourceSaver.Attach();

        // 直接启动
        if (args is LaunchActivatedEventArgs)
        {
            if (_rootFrame.Content == null)
            {
                NavigateToRootPage(args);
                Window.Current.Activate();
            }

            if (!_playbackMemoryRestoreRequested)
            {
                _playbackMemoryRestoreRequested = true;
                await AppDepository.Resolve<IPlaybackMemoryService>().RestoreAsync();
            }
        }
        // 本地播放
        else if (args is FileActivatedEventArgs)
        {
            var playCore = AppDepository.Resolve<PlayCoreBase>();
            var localFileImport = AppDepository.Resolve<ILocalFileImportService>();
            var history = AppDepository.Resolve<IHistoryService>();
            var playbackMemory = AppDepository.Resolve<IPlaybackMemoryService>();
            playCore.PlaySourceId = "local";
            await history.ClearCurrentPlayingListHistoryAsync();
            await playbackMemory.ClearAsync();

            NavigateToRootPage();
            Window.Current.Activate();
            var control = AppDepository.Resolve<IPlaybackControlService>();
            await control.InitializeAsync();
            await control.StopAsync();
            await control.ClearQueueAsync();
            LocalSong firstOpened = null;
            foreach (var storageItem in (args?.As<FileActivatedEventArgs>()).Files)
            {
                if (storageItem is not StorageFile file) continue;
                await localFileImport.RegisterFutureAccessAsync(file);
                var item = await localFileImport.LoadStorageFileAsync(file);
                await playCore.InsertSongAsync(item);
                firstOpened ??= item;
            }

            playCore.PlaySourceId = "local";
            if (firstOpened is not null)
            {
                await playCore.MovePointerToAsync(firstOpened);
                if (playCore.CurrentSong is { } song)
                {
                    await control.LoadAndPlayAsync(song, removeCurrentSongs: false);
                    AppDepository.Resolve<IPlaybackSurfaceCoordinator>().Expand();
                }
            }
        }
    }

    private void NavigateToRootPage(IActivatedEventArgs args = null)
    {
        _rootFrame.Navigate(typeof(MainPage), args?.As<LaunchActivatedEventArgs>()?.Arguments);
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
        try
        {
        _maintenancePause = Task.WhenAll(
            AppDepository.Resolve<IPlaybackMemoryService>().SaveNowAsync(),
            AppDepository.Resolve<IMemoryGovernor>().PauseAsync(),
            DownloadedTrackIndex.StopAsync(), StorageMaintenance.StopAsync(),
            HyPlayer.Features.Playback.AutoMix.AutoMixLog.StopAsync(), ResourceSaver.StopAsync(),
            AppDepository.Resolve<IDynamicCoverService>().ClearCacheAsync());
        await _maintenancePause;
        AppDepository.Resolve<IGameBarWidgetService>().Widget?.Close();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Application suspension cleanup failed: {ex}");
        }
        finally { deferral.Complete(); }
    }

    private async void OnResuming(object sender, object args)
    {
        try { await ResumeMaintenanceAsync(); }
        catch (Exception ex) { System.Diagnostics.Trace.WriteLine($"Application maintenance resume failed: {ex}"); }
    }

    private async Task ResumeMaintenanceAsync()
    {
        // An interrupted suspension can still have native I/O unwinding. Do not
        // replace its lifetime until all owners have finished their StopAsync.
        try { await _maintenancePause; }
        catch (Exception ex) { System.Diagnostics.Trace.WriteLine($"Previous maintenance stop failed: {ex}"); }
        AppDepository.Resolve<IMemoryGovernor>().Resume();
        DownloadedTrackIndex.Resume();
        StorageMaintenance.Resume();
        DownloadedTrackIndex.ScheduleScan(TimeSpan.FromSeconds(40));
        StorageMaintenance.ScheduleTrim(TimeSpan.FromSeconds(30));
        HyPlayer.Features.Playback.AutoMix.AutoMixLog.Resume();
        ResourceSaver.Resume();
    }
}
