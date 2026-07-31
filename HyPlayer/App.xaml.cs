#region

using Depository.Extensions.DependencyInjection;
using CommunityToolkit.Mvvm.DependencyInjection;
using HyPlayer.Composition;
using HyPlayer.Classes;
using HyPlayer.Domain.Settings;
using HyPlayer.Domain;
using HyPlayer.PlayCore.Abstraction;
using HyPlayer.Application.Diagnostics;
using HyPlayer.Application.Notifications;
using HyPlayer.Application.State;
using HyPlayer.Application.Threading;
using HyPlayer.Features.Account.Services;
using HyPlayer.Features.Downloads.Services;
using HyPlayer.Features.History.Services;
using HyPlayer.Features.LastFM.Services;
using HyPlayer.Features.Lyrics.Services;
using HyPlayer.Features.Playback.QueueProviders;
using HyPlayer.Features.Playback.Services;
using HyPlayer.Features.Widgets.Services;
using HyPlayer.Platform.Runtime;
using HyPlayer.Platform.Runtime.Background;
using HyPlayer.Platform.Storage;
using HyPlayer.Platform.SystemServices;
using HyPlayer.Platform.Tiles;
using HyPlayer.Shell.Navigation.Services;
using HyPlayer.Shell.Playback;
using HyPlayer.Shell.Services;
using HyPlayer.UI.Playback.PlayBar;
using HyPlayer.UI.TeachingTips;
using HyPlayer.Platform.Storage.Cache;
using HyPlayer.UWP.Chopin.Abstractions.Models;
using Kawazu;
using Microsoft.Gaming.XboxGameBar;
using System;
using System.Threading.Tasks;
using Windows.ApplicationModel;
using Windows.ApplicationModel.Activation;
using Windows.Storage;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;
using WinRT;
using WidgetPage = HyPlayer.Features.Widgets.WidgetPage;
using WidgetSettingsPage = HyPlayer.Features.Widgets.WidgetSettingsPage;
using HyPlayer.Application;

#endregion

namespace HyPlayer;

/// <summary>
///     提供特定于应用程序的行为，以补充默认的应用程序类。
/// </summary>
public sealed partial class App : Windows.UI.Xaml.Application
{
    /// <summary>
    ///     初始化单一实例应用程序对象。这是执行的创作代码的第一行，
    ///     已执行，逻辑上等同于 main() 或 WinMain()。
    /// </summary>
    private Frame rootFrame;
    private bool _playbackMemoryRestoreRequested;

    public App()
    {
        Suspending += OnSuspending;
        UnhandledException += App_UnhandledException;
        EnteredBackground += App_EnteredBackground;
        LeavingBackground += App_LeavingBackground;
        InitializeComponent();
        InitializeServices();
        InitializeCommonServices();
        AppDepository.Resolve<IHistoryService>().InitializeHistoryTrack();
        _ = AppDepository.Resolve<IPlaybackMemoryService>().InitializeAsync();
        if (AppDepository.Resolve<Setting>().themeRequest != ThemeRequest.Auto)
            RequestedTheme = AppDepository.Resolve<Setting>().themeRequest == ThemeRequest.Light ? ApplicationTheme.Light : ApplicationTheme.Dark;
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
        var setting = AppDepository.Resolve<Setting>();
        var neteaseProvider = AppDepository.Resolve<global::HyPlayer.NeteaseProvider.NeteaseProvider>();
        neteaseProvider.ImportAdditionalConfiguration(setting.ApiAdditionalParametersJson);
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

    private static async Task InitializeThings()
    {
        try
        {
            await SimpleCacher.InitializeAsync();
            await AppDepository.Resolve<HyPlayer.Features.Lyrics.Effects.ILyricEffectProfileService>().InitializeAsync();
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
                    AppDepository.Resolve<ILastFmService>().CompleteBrowserLoginAsync(launchUri.Query.Replace("?token=", string.Empty)),
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
            AppDepository.Resolve<IBackgroundTaskRunner>().Forget(
                control.InitializeAsync(),
                "initialize player for file activation");
            foreach (var storageItem in (args?.As<FileActivatedEventArgs>()).Files)
            {
                var file = (StorageFile)storageItem;
                await localFileImport.RegisterFutureAccessAsync(file);
                var item = await localFileImport.LoadStorageFileAsync(file);
                await playCore.InsertSongAsync(item);
            }

            playCore.PlaySourceId = "local";
            if ((await playCore.GetPlaylistAsync()).Count > 0)
            {
                await playCore.MovePointerToIndexAsync(0);
                if (playCore.CurrentSong is { } song)
                    await control.LoadAndPlayAsync(song, removeCurrentSongs: false);
            }
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
        await AppDepository.Resolve<IPlaybackMemoryService>().SaveNowAsync();
        AppDepository.Resolve<IGameBarWidgetService>().Widget?.Close();
        deferral.Complete();
    }
}
