#region

using CommunityToolkit.Mvvm.DependencyInjection;
using CommunityToolkit.Mvvm.Messaging;
using HyPlayer.Classes;
using HyPlayer.Controls;
using HyPlayer.NeteaseApi;
using HyPlayer.Pages;
using HyPlayer.Services.Abstractions;
using HyPlayer.Services;
using HyPlayer.Services.Playback;
using HyPlayer.Services.Playback.MediaProviders;
using HyPlayer.Services.Playback.Messages;
using HyPlayer.Services.Playback.Strategies;
using HyPlayer.Services.Playback.Transitions;
using HyPlayer.UWP.Chopin;
using HyPlayer.UWP.Chopin.Abstractions.Interfaces;
using HyPlayer.UWP.Chopin.Abstractions.Models;
using HyPlayer.ViewModels;
using Kawazu;
using LiteFM;
using LiteFM.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Gaming.XboxGameBar;
using System;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Windows.ApplicationModel;
using Windows.ApplicationModel.Activation;
using Windows.Storage;
using Windows.Storage.AccessCache;
using Windows.System;
using Windows.UI.StartScreen;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;
using WinRT;
using UnhandledExceptionEventArgs = System.UnhandledExceptionEventArgs;
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
        if (Ioc.Default.GetRequiredService<Setting>().xboxHidePointer)
        {
            RequiresPointerMode = ApplicationRequiresPointerMode.WhenRequested;
            FocusVisualKind = FocusVisualKind.Reveal;
        }
        Suspending += OnSuspending;
        MemoryManager.AppMemoryUsageIncreased += MemoryManagerOnAppMemoryUsageIncreased;
        MemoryManager.AppMemoryUsageLimitChanging += MemoryManagerOnAppMemoryUsageLimitChanging;
        UnhandledException += App_UnhandledException;
        EnteredBackground += App_EnteredBackground;
        LeavingBackground += App_LeavingBackground;
        if (Ioc.Default.GetRequiredService<Setting>().themeRequest != ThemeRequest.Auto)
            RequestedTheme = Ioc.Default.GetRequiredService<Setting>().themeRequest == ThemeRequest.Light ? ApplicationTheme.Light : ApplicationTheme.Dark;
        _ = InitializeThings();
    }

    private static void InitializeServices()
    {
        var serviceCollection = new ServiceCollection();
        InitializeServices(serviceCollection);
        var provider = serviceCollection.BuildServiceProvider();
        Ioc.Default.ConfigureServices(provider);
    }

    private static readonly object _messagingAnchor = new();

    private static void InitializeCommonServices()
    {
        var setting = Ioc.Default.GetRequiredService<Setting>();
        var api = Ioc.Default.GetRequiredService<NeteaseCloudMusicApiHandler>();
        api.Option.AdditionalParameters = setting.ApiAdditionalParameters;
        api.Option.FakeCheckToken = setting.EnableCheckTokenApi;
        var uiState = Ioc.Default.GetRequiredService<IUIStateService>();
        WeakReferenceMessenger.Default.Register<PositionTickMessage>(_messagingAnchor, (_, _) =>
        {
            uiState.RollTeachingTip();
            uiState.ChangePlaybarVisibility();
        });
    }

    private void MemoryManagerOnAppMemoryUsageLimitChanging(object sender, AppMemoryUsageLimitChangingEventArgs e)
    {
        if (!Ioc.Default.GetRequiredService<Setting>().forceMemoryGarbage) return;
        // Xbox 求你行行好,别杀我~ QAQ
        if (!Ioc.Default.GetRequiredService<IUIStateService>().IsInBackground) return;
        // 内存占用达到某个值
        Ioc.Default.GetRequiredService<INavigationService>().CollectGarbage();
        GC.Collect();
    }

    private static void InitializeServices(ServiceCollection serviceCollection)
    {
        var setting = new Setting();
        var handler = NeteaseCloudMusicApiHandler.HttpClientHandler;
        handler.UseProxy = setting.EnableProxy;
        var client = new HttpClient(handler);
        serviceCollection.AddSingleton(client);
        serviceCollection.AddSingleton(new NeteaseCloudMusicApiHandler(client));
        serviceCollection.AddSingleton(new LastFMClient(new LastFMOptions() { ApiKey = LastFMConstants.APIKEY, ApiSecret = LastFMConstants.SECRET }, client));
        serviceCollection.AddSingleton(setting);
        serviceCollection.AddSingleton<AudioGraphPlayer>();
        serviceCollection.AddSingleton<IPlayer>(sp => sp.GetRequiredService<AudioGraphPlayer>());

        // ── 播放核心：状态中心 ──
        serviceCollection.AddSingleton<PlaybackStateService>();

        // ── 播放核心：媒体源 Provider 链（注册顺序 = 优先级）──
        serviceCollection.AddSingleton<IMediaSourceProvider, NcmFileProvider>();           // ncm — NCM 加密文件
        serviceCollection.AddSingleton<IMediaSourceProvider, NeteaseLocalFileProvider>();   // nlo — 网易云已下载到本地
        serviceCollection.AddSingleton<IMediaSourceProvider, LocalFileProvider>();          // lcl — 普通本地文件
        serviceCollection.AddSingleton<IMediaSourceProvider, CachedNeteaseProvider>();      // nca — 网易云在线 + 缓存
        serviceCollection.AddSingleton<IMediaSourceProvider, NeteaseStreamingProvider>();   // nst — 网易云纯流式
        serviceCollection.AddSingleton<IMediaSourceService, MediaSourceService>();

        // ── 播放核心：播放策略 ──
        serviceCollection.AddSingleton<IPlayStrategy, SequentialStrategy>();       // seq — 列表循环
        serviceCollection.AddSingleton<IPlayStrategy, SingleRepeatStrategy>();     // sgl — 单曲循环
        serviceCollection.AddSingleton<IPlayStrategy, ShuffleStrategy>();          // shf — 随机播放
        serviceCollection.AddSingleton<IPlayStrategy, ShuffleNoRepeatStrategy>();  // shn — 随机不重复
        serviceCollection.AddSingleton<IPlayStrategy, PersonalFmStrategy>();       // pfm — 私人 FM
        serviceCollection.AddSingleton<IPlayStrategy, ListenTogetherStrategy>();   // ltg — 一起听

        // ── 播放核心：曲目过渡策略 ──
        serviceCollection.AddSingleton<ITrackTransition, DirectTransition>();      // dir — 直接切歌
        serviceCollection.AddSingleton<ITrackTransition, CrossFadeTransition>();   // xfd — 交叉淡入淡出
        serviceCollection.AddSingleton<ITrackTransition, GaplessTransition>();     // gap — 无缝衔接

        // ── 播放核心：服务 ──
        serviceCollection.AddSingleton<IPlaybackControlService, PlaybackControlService>();
        serviceCollection.AddSingleton<IPlaylistService, PlaylistService>();
        serviceCollection.AddSingleton<ILyricService, LyricService>();
        serviceCollection.AddSingleton<IPlaybackNotificationService, PlaybackNotificationService>();

        // ── 应用核心：认证 / 导航 / 通知 / UI 状态 ──
        serviceCollection.AddSingleton<IAuthService, AuthService>();
        serviceCollection.AddSingleton<INavigationService, NavigationService>();
        serviceCollection.AddSingleton<INotificationService, NotificationService>();
        serviceCollection.AddSingleton<IUIStateService, UIStateService>();

        // ── ViewModels ──
        serviceCollection.AddTransient<HomeViewModel>();
        serviceCollection.AddTransient<MeViewModel>();
        serviceCollection.AddTransient<ExpandedPlayerViewModel>();
        serviceCollection.AddTransient<ArtistPageViewModel>();
        serviceCollection.AddTransient<SongListViewModel>();
        serviceCollection.AddTransient<FavoriteViewModel>();
        serviceCollection.AddTransient<AlbumPageViewModel>();
        serviceCollection.AddTransient<PlayBarViewModel>();
    }
    private void MemoryManagerOnAppMemoryUsageIncreased(object sender, object e)
    {
        if (!Ioc.Default.GetRequiredService<Setting>().forceMemoryGarbage) return;
        if (Ioc.Default.GetRequiredService<IUIStateService>().IsInBackground)
        {
            // 内存占用达到某个值
            Ioc.Default.GetRequiredService<INavigationService>().CollectGarbage();
            GC.Collect();
        }
    }

    private static async Task InitializeThings()
    {
        try
        {
            await SimpleCacher.InitializeAsync();
            var sf = await ApplicationData.Current.LocalCacheFolder.TryGetItemAsync("Romaji");
            if (sf != null) Ioc.Default.GetRequiredService<IUIStateService>().KawazuConv = new KawazuConverter(sf.Path);
        }
        catch
        {
            // ignored
        }

        if (Ioc.Default.GetRequiredService<IUIStateService>().IsExpanded)
            _ = Ioc.Default.GetRequiredService<INotificationService>().InvokeOnUIThread(() => { (Ioc.Default.GetRequiredService<IUIStateService>().PageMain as MainPage).ExpandedPlayer.Navigate(typeof(ExpandedPlayer)); });
    }

    private void App_LeavingBackground(object sender, LeavingBackgroundEventArgs e)
    {
        var uiState = Ioc.Default.GetRequiredService<IUIStateService>();
        if (uiState.IsInBackground)
        {
            uiState.IsInBackground = false;
            uiState.InvokeEnterForeground();
        }

        uiState.IsInBackground = false;

        if (!Ioc.Default.GetRequiredService<Setting>().forceMemoryGarbage) return;
        Ioc.Default.GetRequiredService<INavigationService>().NavigateBack();

        //ClearExtendedExecution(executionSession);
    }

    private void App_EnteredBackground(object sender, EnteredBackgroundEventArgs e)
    {
        Ioc.Default.GetRequiredService<IUIStateService>().IsInBackground = true;
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
                    Ioc.Default.GetRequiredService<IUIStateService>().XboxGameBarWidget = new XboxGameBarWidget(
                        widgetArgs,
                        Window.Current.CoreWindow,
                        widgetFrame);
                    widgetFrame.Navigate(typeof(WidgetPage), (Ioc.Default.GetRequiredService<IUIStateService>().XboxGameBarWidget as XboxGameBarWidget));

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
            if (Ioc.Default.GetRequiredService<IUIStateService>().IsExpanded) return;
            var animation = Ioc.Default.GetRequiredService<Setting>().expandAnimation;
            Ioc.Default.GetRequiredService<Setting>().expandAnimation = false;
            (Ioc.Default.GetRequiredService<IUIStateService>().BarPlayBar as PlayBar).ShowExpandedPlayer();
            var a = Ioc.Default.GetRequiredService<Setting>().expandAnimation;
            Ioc.Default.GetRequiredService<Setting>().expandAnimation = animation;
        }
        if (args.Kind == ActivationKind.Protocol)
        {
            var launchUri = (args?.As<IProtocolActivatedEventArgs>())?.Uri;
            if (launchUri?.Host == "link.last.fm")
                _ = LastFMManager.TryLoginLastfmAccountFromBrowser(launchUri.Query.Replace("?token=", string.Empty));
        }
    }

    private void App_UnhandledException(object sender, Windows.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        Ioc.Default.GetRequiredService<IUIStateService>().ErrorMessageList.Add(e.Exception.ToString());
        e.Handled = true;
    }

    public static async Task InitializeJumpList()
    {
        var jumpList = await JumpList.LoadCurrentAsync();
        jumpList.Items.Clear();

        var item1 = JumpListItem.CreateWithArguments("search", "搜索");
        item1.Logo = new Uri("ms-appx:///Assets/JumpListIcons/JumplistSearch.png");
        if (Ioc.Default.GetRequiredService<IAuthService>().IsLoggedIn)
        {
            var item2 = JumpListItem.CreateWithArguments("account", "账户");
            item2.Logo = new Uri("ms-appx:///Assets/JumpListIcons/JumplistAccount.png");
            var item3 = JumpListItem.CreateWithArguments("likedsongs", "我喜欢的音乐");
            item3.Logo = new Uri("ms-appx:///Assets/JumpListIcons/JumplistLikedSongs.png");
            jumpList.Items.Add(item2);
            jumpList.Items.Add(item3);
        }

        var item4 = JumpListItem.CreateWithArguments("local", "本地音乐");
        item4.Logo = new Uri("ms-appx:///Assets/JumpListIcons/JumplistLocal.png");

        jumpList.Items.Add(item1);

        jumpList.Items.Add(item4);
        await jumpList.SaveAsync();
    }

    protected override void OnFileActivated(FileActivatedEventArgs args) => OnLaunchedOrActivatedAsync(args);

    protected override void OnLaunched(LaunchActivatedEventArgs args) => OnLaunchedOrActivatedAsync(args);

    private async void OnLaunchedOrActivatedAsync(IActivatedEventArgs args)
    {
        _ = InitializeJumpList();

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
            var _playlist = Ioc.Default.GetRequiredService<IPlaylistService>();
            _playlist.PlaySourceId = "local";
            Ioc.Default.GetRequiredService<IUIStateService>().IsExpanded = true;
            ApplicationData.Current.LocalSettings.Values["curPlayingListHistory"] = "[]";

            NavigateToRootPage();
            Window.Current.Activate();
            var _control = Ioc.Default.GetRequiredService<IPlaybackControlService>();
            var _player = Ioc.Default.GetRequiredService<AudioGraphPlayer>();
            if (!_player.PlayerCreated)
            {
                _ = _control.InitializeAsync();
            }
            foreach (var storageItem in (args?.As<FileActivatedEventArgs>()).Files)
            {
                var file = (StorageFile)storageItem;
                var folder = await file.GetParentAsync();
                if (folder != null)
                {
                    if (!StorageApplicationPermissions.FutureAccessList.ContainsItem(folder.Path.GetHashCode().ToString()))
                        StorageApplicationPermissions.FutureAccessList.AddOrReplace(folder.Path.GetHashCode().ToString(),
                            folder);
                }
                else
                {
                    if (!StorageApplicationPermissions.FutureAccessList.ContainsItem(file.Path.GetHashCode().ToString()))
                        StorageApplicationPermissions.FutureAccessList.AddOrReplace(file.Path.GetHashCode().ToString(),
                            file);
                }

                var item = await _playlist.LoadStorageFileAsync(file);
                _playlist.AppendItem(item);
            }

            _playlist.PlaySourceId = "local";
            _playlist.NotifyAppendDone();
            if (_playlist.Items.Count > 0)
                await _playlist.MoveToAsync(_playlist.Items[0]);
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
        var _playlist = Ioc.Default.GetRequiredService<IPlaylistService>();
        await HistoryManagement.SetcurPlayingListHistory([.. _playlist.Items
            .Where(t => t.ItemType == HyPlayItemType.Netease)
            .Select(t => t.Id)]);
        (Ioc.Default.GetRequiredService<IUIStateService>().XboxGameBarWidget as XboxGameBarWidget)?.Close();
        deferral.Complete();
    }
}