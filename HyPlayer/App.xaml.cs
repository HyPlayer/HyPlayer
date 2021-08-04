using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using Windows.ApplicationModel;
using Windows.ApplicationModel.Activation;
using Windows.Storage;
using Windows.UI.StartScreen;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;
using HyPlayer.HyPlayControl;
using Microsoft.AppCenter;
using Microsoft.AppCenter.Analytics;
using Microsoft.AppCenter.Crashes;
using UnhandledExceptionEventArgs = System.UnhandledExceptionEventArgs;
using Windows.ApplicationModel.ExtendedExecution;
using Windows.System;
using Windows.System.Profile;
using HyPlayer.Pages;
using Kawazu;
using HyPlayer.Classes;
using Windows.Security.ExchangeActiveSyncProvisioning;

namespace HyPlayer
{
    /// <summary>
    ///     提供特定于应用程序的行为，以补充默认的应用程序类。
    /// </summary>
    sealed partial class App : Application
    {
        /// <summary>
        ///     初始化单一实例应用程序对象。这是执行的创作代码的第一行，
        ///     已执行，逻辑上等同于 main() 或 WinMain()。
        /// </summary>
        ExtendedExecutionSession executionSession = null;

        private bool isInBackground = false;

        public App()
        {
            InitializeComponent();
            Suspending += OnSuspending;
            UnhandledException += App_UnhandledException;
            EnteredBackground += App_EnteredBackground;
            LeavingBackground += App_LeavingBackground;
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
            AppCenter.Start("8e88eab0-1627-4ff9-9ee7-7fd46d0629cf",
                typeof(Analytics), typeof(Crashes));
            AppCenter.SetEnabledAsync(true);
            EasClientDeviceInformation deviceInfo = new EasClientDeviceInformation();
            AppCenter.SetUserId(deviceInfo.FriendlyName);
            MemoryManager.AppMemoryUsageIncreased += MemoryManagerOnAppMemoryUsageIncreased;
            MemoryManager.AppMemoryUsageLimitChanging += MemoryManagerOnAppMemoryUsageLimitChanging;
            if (Common.Setting.themeRequest != 0)
                RequestedTheme = Common.Setting.themeRequest == 1 ? ApplicationTheme.Light : ApplicationTheme.Light;
            InitializeThings();
        }

        private void MemoryManagerOnAppMemoryUsageLimitChanging(object sender, AppMemoryUsageLimitChangingEventArgs e)
        {
            Common.Invoke(() =>
            {
                // Xbox 求你行行好,别杀我~ QAQ
                if (isInBackground)
                {
                    // 内存占用达到某个值
                    Common.CollectGarbage();
                    GC.Collect();
                }


                // 追踪代码
                Crashes.TrackError(new Exception("MemoryManagerOnAppMemoryUsageLimitChanging"), new Dictionary<string, string>()
            {
                {"ListCount", HyPlayList.List.Count.ToString()},
                {"NowMemory", MemoryManager.AppMemoryUsage.ToString()},
                {"DesireMemory", MemoryManager.AppMemoryUsageLimit.ToString()},
                {"TotalCommitLimit", MemoryManager.GetAppMemoryReport().TotalCommitLimit.ToString()},
                {"IsInBackground", isInBackground.ToString()},
                {"OldSize", e.OldLimit.ToString()},
                {"NewSize", e.NewLimit.ToString()},
                {"DeviceFamily",AnalyticsInfo.VersionInfo.DeviceFamily},
                {"DeviceFamilyVersion",AnalyticsInfo.VersionInfo.DeviceFamilyVersion}
            });
            });

        }

        private void MemoryManagerOnAppMemoryUsageIncreased(object sender, object e)
        {
            Common.Invoke(() =>
            {
                if (isInBackground)
                {
                    // 内存占用达到某个值
                    Common.CollectGarbage();
                    GC.Collect();
                }


                // 追踪代码
                Crashes.TrackError(new Exception("MemoryManagerOnAppMemoryUsageIncreased"), new Dictionary<string, string>()
            {
                {"ListCount", HyPlayList.List.Count.ToString()},
                {"NowMemory", MemoryManager.AppMemoryUsage.ToString()},
                {"DesireMemory", MemoryManager.AppMemoryUsageLimit.ToString()},
                {"TotalCommitLimit", MemoryManager.GetAppMemoryReport().TotalCommitLimit.ToString()},
                {"IsInBackground", isInBackground.ToString()},
                {"DeviceFamily",AnalyticsInfo.VersionInfo.DeviceFamily},
                {"DeviceFamilyVersion",AnalyticsInfo.VersionInfo.DeviceFamilyVersion}
            });
            });
        }


        private void InitializeThings()
        {
            Common.Invoke(async () =>
            {
                try
                {
                    var sf = await ApplicationData.Current.LocalCacheFolder.GetFolderAsync("Romaji");
                    Common.KawazuConv = new KawazuConverter(sf.Path);
                }
                catch
                {
                }

                if (Common.isExpanded)
                    Common.PageMain.ExpandedPlayer.Navigate(typeof(ExpandedPlayer));
            });
        }

        private async void App_LeavingBackground(object sender, LeavingBackgroundEventArgs e)
        {
            isInBackground = false;
            InitializeThings();
            ClearExtendedExecution(executionSession);
            Common.NavigateBack();
        }

        private async void App_EnteredBackground(object sender, EnteredBackgroundEventArgs e)
        {
            isInBackground = true;
            var delaySession = new ExtendedExecutionSession();
            delaySession.Reason = ExtendedExecutionReason.Unspecified;
            delaySession.Revoked += SessionRevoked;
            ExtendedExecutionResult result = await delaySession.RequestExtensionAsync();
            switch (result)
            {
                case ExtendedExecutionResult.Allowed:
                    executionSession = delaySession;
                    _ = Task.Run(() => Common.Invoke(async () =>
                    {
                        Common.CollectGarbage();
                        await Task.Delay(1000);
                        GC.Collect();
                    }));

                    break;
                case ExtendedExecutionResult.Denied:
                    /*
                    var toast = new Microsoft.Toolkit.Uwp.Notifications.ToastContentBuilder();
                    toast.AddText("应用程序进入后台，有可能关闭");
                    toast.Show();
                    */
                    break;
            }
        }

        private void ClearExtendedExecution(ExtendedExecutionSession session)
        {
            if (session != null)
            {
                session.Revoked -= SessionRevoked;
                session.Dispose();
                session = null;
            }
        }

        private async void SessionRevoked(object sender, ExtendedExecutionRevokedEventArgs args)
        {
            await Windows.ApplicationModel.Core.CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(
                Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                {
                    switch (args.Reason)
                    {
                        case ExtendedExecutionRevokedReason.Resumed:
                            executionSession.Revoked -= SessionRevoked;
                            executionSession.Dispose();
                            executionSession = null;
                            break;

                        case ExtendedExecutionRevokedReason.SystemPolicy:
                            /*
                            var toast = new Microsoft.Toolkit.Uwp.Notifications.ToastContentBuilder();
                            toast.AddText("应用程序进入后台，有可能关闭");
                            toast.Show();
                            */
                            break;
                    }
                });
        }

        protected override void OnActivated(IActivatedEventArgs args)
        {
            base.OnActivated(args);
            if (args.Kind == ActivationKind.ToastNotification) //如果用户点击了桌面歌词通知，则代表通知已经关闭，需要重新初始化推送
                if (Common.BarPlayBar != null)
                    Common.BarPlayBar.InitializeDesktopLyric();
        }

        private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
#if RELEASE
            Crashes.TrackError((Exception)e.ExceptionObject);
#endif
            Common.Invoke(async () =>
            {
                var Dialog = new ContentDialog
                {
                    Title = "遇到了错误",
                    Content = e.ExceptionObject.ToString(),
                    PrimaryButtonText = "退出"
                };
                var result = await Dialog.ShowAsync();
                Environment.Exit(0);
            });
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

        public async void InitializeJumpList()
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

        protected override async void OnFileActivated(FileActivatedEventArgs args)
        {
            InitializeJumpList();
            var rootFrame = Window.Current.Content as Frame;
            if (rootFrame == null)
            {
                rootFrame = new Frame();
                Window.Current.Content = rootFrame;
            }

            rootFrame.Navigate(typeof(MainPage));
            Window.Current.Activate();
            HyPlayList.RemoveAllSong();
            foreach (StorageFile file in args.Files) await HyPlayList.AppendStorageFile(file);
            HyPlayList.SongAppendDone();
            HyPlayList.SongMoveTo(0);
        }

        /// <summary>
        ///     在应用程序由最终用户正常启动时进行调用。
        ///     将在启动应用程序以打开特定文件等情况下使用。
        /// </summary>
        /// <param name="e">有关启动请求和过程的详细信息。</param>
        protected override void OnLaunched(LaunchActivatedEventArgs e)
        {
            InitializeJumpList();
            var rootFrame = Window.Current.Content as Frame;

            // 不要在窗口已包含内容时重复应用程序初始化，
            // 只需确保窗口处于活动状态
            if (rootFrame == null)
            {
                // 创建要充当导航上下文的框架，并导航到第一页
                rootFrame = new Frame();

                rootFrame.NavigationFailed += OnNavigationFailed;

                if (e.PreviousExecutionState == ApplicationExecutionState.Terminated)
                {
                    //TODO: 从之前挂起的应用程序加载状态
                }

                // 将框架放在当前窗口中
                Window.Current.Content = rootFrame;
            }

            if (e.PrelaunchActivated == false)
            {
                rootFrame.Navigate(typeof(MainPage), e.Arguments);

                // 确保当前窗口处于活动状态
                Window.Current.Activate();
            }
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
        private void OnSuspending(object sender, SuspendingEventArgs e)
        {
            var deferral = e.SuspendingOperation.GetDeferral();
            //TODO: 保存应用程序状态并停止任何后台活动
            deferral.Complete();
        }
    }
}