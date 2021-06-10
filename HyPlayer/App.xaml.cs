﻿using Microsoft.Toolkit.Uwp.Notifications;
using System;
using Windows.ApplicationModel;
using Windows.ApplicationModel.Activation;
using Windows.UI.Notifications;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;
using Microsoft.AppCenter;
using Microsoft.AppCenter.Analytics;
using Microsoft.AppCenter.Crashes;
using UnhandledExceptionEventArgs = Windows.UI.Xaml.UnhandledExceptionEventArgs;

namespace HyPlayer
{
    /// <summary>
    /// 提供特定于应用程序的行为，以补充默认的应用程序类。
    /// </summary>
    sealed partial class App : Application
    {
        /// <summary>
        /// 初始化单一实例应用程序对象。这是执行的创作代码的第一行，
        /// 已执行，逻辑上等同于 main() 或 WinMain()。
        /// </summary>
        public App()
        {
            InitializeComponent();
            Suspending += OnSuspending;
            UnhandledException += App_UnhandledException;
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
            AppCenter.Start("8e88eab0-1627-4ff9-9ee7-7fd46d0629cf",
                typeof(Analytics), typeof(Crashes));
        }
        protected override void OnActivated(IActivatedEventArgs args)
        {
            base.OnActivated(args);
            if (args.Kind == ActivationKind.ToastNotification)//如果用户点击了桌面歌词通知，则代表通知已经关闭，需要重新初始化推送
                InitializeToastLyrics();
        }
        public void InitializeToastLyrics()
        {
            ToastContentBuilder desktopLyricsToast = new ToastContentBuilder();
            desktopLyricsToast.SetToastScenario(ToastScenario.IncomingCall);
            desktopLyricsToast.AddAudio(new ToastAudio() { Silent = true });
            desktopLyricsToast.AddVisualChild(new AdaptiveText()
            {
                Text = new BindableString("Title"),
                HintStyle = AdaptiveTextStyle.Header
            });
            desktopLyricsToast.AddVisualChild(new AdaptiveText()
            {
                Text = new BindableString("PureLyric"),
            });
            desktopLyricsToast.AddVisualChild(new AdaptiveText()
            {
                Text = new BindableString("Translation"),
            });
            desktopLyricsToast.AddVisualChild(new AdaptiveProgressBar()
            {
                ValueStringOverride = new BindableString("TotalValueString"),

                Status = new BindableString("CurrentValueString"),

                Value = new BindableProgressBarValue("CurrentValue"),

            });


            /*
            ToastContent desktopLyrics = new ToastContent()
            {
                Visual = new ToastVisual()
                {
                    BindingGeneric = new ToastBindingGeneric()
                    {
                        Children =
                            {
                                new AdaptiveText()
                                {
                                    Text = new BindableString("Title"),
                                    HintStyle = AdaptiveTextStyle.Header
                                },
                                new AdaptiveText()
                                {
                                    Text = new BindableString("PureLyric"),
                                },
                                new AdaptiveText()
                                {
                                    Text = new BindableString("Translation"),
                                },
                                new AdaptiveProgressBar()
                                {
                                    ValueStringOverride=new BindableString("TotalValueString"),

                                    Status=new BindableString("CurrentValueString"),

                                    Value=new BindableProgressBarValue("CurrentValue"),

                                },
                            }
                    }
                },
                Launch = "",
                Scenario = ToastScenario.IncomingCall,
                Audio = new ToastAudio() { Silent = true },


            };
            */
            var toast = new ToastNotification(desktopLyricsToast.GetXml())
            {
                Tag = "HyPlayerDesktopLyrics",
                Data = new NotificationData()
            };
            toast.Data.Values["Title"] = "当前无音乐播放";
            toast.Data.Values["PureLyric"] = "当前无歌词";
            toast.Data.Values["TotalValueString"] = "0:00:00";
            toast.Data.Values["CurrentValueString"] = "0:00:00";
            toast.Data.Values["CurrentValue"] = "0";

            toast.Data.SequenceNumber = 0;
            ToastNotifier notifier = ToastNotificationManager.CreateToastNotifier();
            notifier.Show(toast);
        }
        private void CurrentDomain_UnhandledException(object sender, System.UnhandledExceptionEventArgs e)
        {
#if RELEASE
            Crashes.TrackError((Exception)e.ExceptionObject);
#endif
            Common.Invoke((async () =>
            {
                ContentDialog Dialog = new ContentDialog
                {
                    Title = "遇到了错误",
                    Content = e.ExceptionObject.ToString(),
                    PrimaryButtonText = "退出"
                };
                ContentDialogResult result = await Dialog.ShowAsync();
                Environment.Exit(0);
            }));

        }

        private void App_UnhandledException(object sender, UnhandledExceptionEventArgs e)
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

        /// <summary>
        /// 在应用程序由最终用户正常启动时进行调用。
        /// 将在启动应用程序以打开特定文件等情况下使用。
        /// </summary>
        /// <param name="e">有关启动请求和过程的详细信息。</param>
        protected override void OnLaunched(LaunchActivatedEventArgs e)
        {
            Frame rootFrame = Window.Current.Content as Frame;

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
                if (rootFrame.Content == null)
                {
                    // 当导航堆栈尚未还原时，导航到第一页，
                    // 并通过将所需信息作为导航参数传入来配置
                    // 参数
                    rootFrame.Navigate(typeof(MainPage), e.Arguments);
                }
                // 确保当前窗口处于活动状态
                Window.Current.Activate();
            }
            if (Common.Setting.toastLyric)
            {
                InitializeToastLyrics();
            }
        }

        /// <summary>
        /// 导航到特定页失败时调用
        /// </summary>
        ///<param name="sender">导航失败的框架</param>
        ///<param name="e">有关导航失败的详细信息</param>
        private void OnNavigationFailed(object sender, NavigationFailedEventArgs e)
        {
            throw new Exception("Failed to load Page " + e.SourcePageType.FullName);
        }

        /// <summary>
        /// 在将要挂起应用程序执行时调用。  在不知道应用程序
        /// 无需知道应用程序会被终止还是会恢复，
        /// 并让内存内容保持不变。
        /// </summary>
        /// <param name="sender">挂起的请求的源。</param>
        /// <param name="e">有关挂起请求的详细信息。</param>
        private void OnSuspending(object sender, SuspendingEventArgs e)
        {
            SuspendingDeferral deferral = e.SuspendingOperation.GetDeferral();
            //TODO: 保存应用程序状态并停止任何后台活动
            deferral.Complete();
        }
    }
}
