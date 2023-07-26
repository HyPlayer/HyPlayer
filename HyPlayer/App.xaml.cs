#region

using HyPlayer.Classes;
using HyPlayer.HyPlayControl;
using HyPlayer.Pages;
using Kawazu;
using Microsoft.AppCenter;
using Microsoft.AppCenter.Analytics;
using Microsoft.AppCenter.Crashes;
using System;
using System.Linq;
using System.Threading.Tasks;
using Windows.ApplicationModel;
using Windows.ApplicationModel.Activation;
using Windows.ApplicationModel.ExtendedExecution;
using Windows.Security.ExchangeActiveSyncProvisioning;
using Windows.Storage;
using Windows.Storage.AccessCache;
using Windows.System;
using Windows.UI.StartScreen;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;
using UnhandledExceptionEventArgs = System.UnhandledExceptionEventArgs;

using SettingsService = HyPlayer.Setting;
#endregion

namespace HyPlayer;

/// <summary>
///     提供特定于应用程序的行为，以补充默认的应用程序类。
/// </summary>
sealed partial class App : Application
{
    /// <summary>
    ///     初始化单一实例应用程序对象。这是执行的创作代码的第一行，
    ///     已执行，逻辑上等同于 main() 或 WinMain()。
    /// </summary>
    private ExtendedExecutionSession executionSession;
    private Frame rootFrame;

    public App()
    {
        InitializeComponent();

        if (Common.Setting.xboxHidePointer)
        {
            RequiresPointerMode = ApplicationRequiresPointerMode.WhenRequested;
            FocusVisualKind = FocusVisualKind.Reveal;
        }


        Suspending += OnSuspending;
        UnhandledException += App_UnhandledException;
        EnteredBackground += App_EnteredBackground;
        LeavingBackground += App_LeavingBackground;
        AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
        AppCenter.Start("8e88eab0-1627-4ff9-9ee7-7fd46d0629cf",
            typeof(Analytics), typeof(Crashes));
        AppCenter.SetEnabledAsync(true);
        var deviceInfo = new EasClientDeviceInformation();
        AppCenter.SetUserId(deviceInfo.Id.ToString());
        MemoryManager.AppMemoryUsageIncreased += MemoryManagerOnAppMemoryUsageIncreased;
        MemoryManager.AppMemoryUsageLimitChanging += MemoryManagerOnAppMemoryUsageLimitChanging;
        if (Common.Setting.themeRequest != 0)
            RequestedTheme = Common.Setting.themeRequest == 1 ? ApplicationTheme.Light : ApplicationTheme.Dark;
        Common.InitializeHttpClientAndAPI();
        _ = InitializeThings();
    }


    private void MemoryManagerOnAppMemoryUsageLimitChanging(object sender, AppMemoryUsageLimitChangingEventArgs e)
    {
        if (!Common.Setting.forceMemoryGarbage) return;
        // Xbox 求你行行好,别杀我~ QAQ
        if (!Common.IsInBackground) return;
        // 内存占用达到某个值
        Common.CollectGarbage();
        GC.Collect();
    }

    private void MemoryManagerOnAppMemoryUsageIncreased(object sender, object e)
    {
        if (!Common.Setting.forceMemoryGarbage) return;
        if (Common.IsInBackground)
        {
            // 内存占用达到某个值
            Common.CollectGarbage();
            GC.Collect();
        }
    }


    private async Task InitializeThings()
    {
        try
        {
            var sf = await ApplicationData.Current.LocalCacheFolder.TryGetItemAsync("Romaji");
            if (sf != null) Common.KawazuConv = new KawazuConverter(sf.Path);
        }
        catch
        {
            // ignored
        }

        if (Common.isExpanded)
            _ = Common.Invoke(() => { Common.PageMain.ExpandedPlayer.Navigate(typeof(ExpandedPlayer)); });
    }

    private void App_LeavingBackground(object sender, LeavingBackgroundEventArgs e)
    {
        if (Common.IsInBackground)
        {
            Common.IsInBackground = false;
            Common.OnEnterForegroundFromBackground?.Invoke();
        }

        Common.IsInBackground = false;

        if (!Common.Setting.forceMemoryGarbage) return;
        _ = InitializeThings();
        Common.NavigateBack();

        //ClearExtendedExecution(executionSession);
    }

    private void App_EnteredBackground(object sender, EnteredBackgroundEventArgs e)
    {
        Common.IsInBackground = true;
    }

    protected override void OnActivated(IActivatedEventArgs args)
    {
        base.OnActivated(args);
        if (args.Kind == ActivationKind.ToastNotification)
        {
            rootFrame = Window.Current.Content as Frame;
            if (rootFrame == null)
            {
                rootFrame = new Frame();
                Window.Current.Content = rootFrame;
            }

            rootFrame.Navigate(typeof(MainPage));
            Window.Current.Activate();
            Common.BarPlayBar.InitializeDesktopLyric();
            if (Common.isExpanded) return;
            var animation = Common.Setting.expandAnimation;
            Common.Setting.expandAnimation = false;
            Common.BarPlayBar.ShowExpandedPlayer();
            var a = Common.Setting.expandAnimation;
            Common.Setting.expandAnimation = animation;
        }
        if (args.Kind == ActivationKind.Protocol)
        {
            var launchUri = ((ProtocolActivatedEventArgs)args).Uri;
            if (launchUri.Host == "link.last.fm") _ = LastFMManager.TryLoginLastfmAccountFromBrowser(launchUri.Query.Replace("?token=", string.Empty));
        }
    }

    private async void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
#if RELEASE
            Crashes.TrackError((Exception)e.ExceptionObject);
#endif

        var Dialog = new ContentDialog
        {
            Title = "遇到了错误",
            Content = e.ExceptionObject.ToString(),
            PrimaryButtonText = "退出"
        };
        var result = await Dialog.ShowAsync();
    }

    private void App_UnhandledException(object sender, Windows.UI.Xaml.UnhandledExceptionEventArgs e)
    {
#if RELEASE
            Crashes.TrackError(e.Exception);
#endif
        e.Handled = true;
        /*
        await new ContentDialog
        {
            Title = "发生错误",
            Content = "Error: " + e.Message + "\r\n" + e.Exception.StackTrace,
            CloseButtonText = "关闭",
            DefaultButton = ContentDialogButton.Close
        }.ShowAsync();
        */
    }

    public async Task InitializeJumpList()
    {
        var jumpList = await JumpList.LoadCurrentAsync();
        jumpList.Items.Clear();

        var item1 = JumpListItem.CreateWithArguments("search", "搜索");
        item1.Logo = new Uri("ms-appx:///Assets/JumpListIcons/JumplistSearch.png");
        if (Common.Logined)
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

        rootFrame = Window.Current.Content as Frame;
        if(rootFrame == null)
        {
            rootFrame = new Frame();
            rootFrame.NavigationFailed += OnNavigationFailed;

            if(args.PreviousExecutionState == ApplicationExecutionState.Terminated)
            {

            }

            Window.Current.Content = rootFrame;
        }

        // 直接启动
        if(args is LaunchActivatedEventArgs)
        {
            if(rootFrame.Content == null)
            {
                NavigateToRootPage(args);
                Window.Current.Activate();
            }

        }
        // 本地播放
        else if(args is FileActivatedEventArgs)
        {
            HyPlayList.PlaySourceId = "local";
            Common.isExpanded = true;
            ApplicationData.Current.LocalSettings.Values["curPlayingListHistory"] = "[]";

            NavigateToRootPage();
            Window.Current.Activate();

            if (HyPlayList.Player == null)
                HyPlayList.InitializeHyPlaylist();
            foreach (var storageItem in (args as FileActivatedEventArgs).Files)
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

                await HyPlayList.AppendStorageFile(file);
            }

            HyPlayList.PlaySourceId = "local";
            HyPlayList.SongMoveTo(0);
        }


    }

    private void NavigateToRootPage(IActivatedEventArgs args = null)
    {
        rootFrame.Navigate(typeof(MainPage), (args as LaunchActivatedEventArgs).Arguments);
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
        await HistoryManagement.SetcurPlayingListHistory(HyPlayList.List
            .Where(t => t.ItemType == HyPlayItemType.Netease)
            .Select(t => t.PlayItem.Id).ToList());
        deferral.Complete();
    }
}