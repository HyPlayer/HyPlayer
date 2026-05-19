#region

using AsyncAwaitBestPractices;
using CommunityToolkit.Mvvm.DependencyInjection;
using CommunityToolkit.Mvvm.Messaging;
using HyPlayer.Classes;
using HyPlayer.Controls;
using HyPlayer.Services.Abstractions;
using HyPlayer.Services.Playback;
using HyPlayer.UWP.Chopin;
using HyPlayer.UWP.Chopin.Abstractions.Models;
using HyPlayer.ViewModels;
using System;
using Windows.System;
using Windows.UI.Core;
using Windows.UI.ViewManagement;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Navigation;
using TeachingTip = Microsoft.UI.Xaml.Controls.TeachingTip;

#endregion


// https://go.microsoft.com/fwlink/?LinkId=402352&clcid=0x804 上介绍了"空白页"项模板

namespace HyPlayer.Pages;

/// <summary>
///     Root shell page: title bar, navigation frame, and global input.
///     Account, login, and search logic are delegated to shell components.
/// </summary>
public sealed partial class BasePage : Page
{
    private readonly Setting _setting = Ioc.Default.GetRequiredService<Setting>();
    private readonly IAppNavigator _navigator = Ioc.Default.GetRequiredService<IAppNavigator>();
    private readonly ShellLoginService _loginService = Ioc.Default.GetRequiredService<ShellLoginService>();
    private readonly NavigationShellViewModel _navigationShell = Ioc.Default.GetRequiredService<NavigationShellViewModel>();
    private readonly IPlaybackControlService _playback = Ioc.Default.GetRequiredService<IPlaybackControlService>();
    private readonly AudioGraphPlayer _player = Ioc.Default.GetRequiredService<AudioGraphPlayer>();
    private readonly IShellHostStateService _shellHost = Ioc.Default.GetRequiredService<IShellHostStateService>();
    private readonly ITeachingTipService _teachingTip = Ioc.Default.GetRequiredService<ITeachingTipService>();

    public BasePage()
    {
        InitializeComponent();
        _shellHost.AppTitleBar = AppTitleBar;
        _teachingTip.Tip = TheTeachingTip;

        if (!_player.PlayerCreated)
        {
            _ = _playback.InitializeAsync();
        }

        ApplicationView.TerminateAppOnFinalViewClose = false;
        _navigator.AttachNavigationView(NavMain, BaseFrame, _navigationShell, _loginService.ShowLoginRequiredDialogAsync);
        _navigationShell.UpdateAccountStatus();
        Window.Current.CoreWindow.KeyDown += CoreWindow_KeyDown;
        Window.Current.CoreWindow.PointerPressed += CoreWindow_PointerPressed;
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        WeakReferenceMessenger.Default.UnregisterAll(this);
        _navigator.DetachNavigationView(NavMain);
        Window.Current.CoreWindow.KeyDown -= CoreWindow_KeyDown;
        Window.Current.CoreWindow.PointerPressed -= CoreWindow_PointerPressed;
        _shellHost.ClearReference(AppTitleBar);
        if (ReferenceEquals(_teachingTip.Tip, TheTeachingTip))
            _teachingTip.Tip = null;
    }


    private void CoreWindow_PointerPressed(CoreWindow sender, PointerEventArgs args)
    {
        if (args.CurrentPoint.Properties.IsXButton1Pressed)
            if (!CollapseExpandedPlayerIfNeeded())
                _navigator.NavigateBack();
    }

    private void CoreWindow_KeyDown(CoreWindow sender, KeyEventArgs args)
    {
        if (args.VirtualKey == VirtualKey.GamepadB)
        {
            if (!CollapseExpandedPlayerIfNeeded())
                _navigator.NavigateBack();
            args.Handled = true;
        }

        if (args.VirtualKey == VirtualKey.GamepadY)
            if (_playback.IsPlaying)
                _player.PauseAll();
            else if (!_playback.IsPlaying) _player.PlayAll();

        if (args.VirtualKey == VirtualKey.Escape)
            CollapseExpandedPlayerIfNeeded();
    }

    private bool CollapseExpandedPlayerIfNeeded()
    {
        var surfaceCoordinator = Ioc.Default.GetRequiredService<IPlaybackSurfaceCoordinator>();
        if (!surfaceCoordinator.IsExpanded) return false;
        surfaceCoordinator.Collapse();
        return true;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (!_setting.DisablePopUp)
        {
            var dialog = new ContentDialog
            {
                Title = "重要提示",
                Content = "本软件仅供学习交流使用，下载后请在 24 小时内删除。\r\n请勿使用此软件登录网易云音乐或进行违反网易云音乐用户协议的行为",
                CloseButtonText = "退出软件",
                PrimaryButtonText = "我已知晓",
                IsPrimaryButtonEnabled = true,
                DefaultButton = ContentDialogButton.Primary
            };
            dialog.CloseButtonClick += (_, _) => _ = ApplicationView.GetForCurrentView().TryConsolidateAsync();
            _ = dialog.ShowAsync();
        }

        // 不要阻塞页面加载
        _ = UpdateManager.PopupVersionCheck(true);
        _ = _loginService.TryLoadSavedLoginAsync();
    }

    private void TheTeachingTip_OnCloseButtonClick(TeachingTip sender, object args)
    {
        Ioc.Default.GetRequiredService<ITeachingTipService>().Clear();
    }

    private void BaseFrame_Navigated(object sender, NavigationEventArgs e)
    {
        _navigator.SyncNavigationViewSelection(e.SourcePageType, e.Parameter);
    }

    // ── TitleBar button handlers ──

    private void AppTitleBar_BackButtonClick(object sender, RoutedEventArgs e)
    {
        _navigator.NavigateBack();
    }

    private void AppTitleBar_PaneButtonClick(object sender, RoutedEventArgs e)
    {
        _navigator.ToggleNavigationPane();
    }
}
