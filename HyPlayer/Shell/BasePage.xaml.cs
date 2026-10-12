#region

using Windows.System;
using Windows.UI.Core;
using Windows.UI.ViewManagement;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;
using CommunityToolkit.Mvvm.DependencyInjection;
using HyPlayer.Domain.Settings;
using HyPlayer.Features.Playback.Services;
using HyPlayer.Features.Settings.Services;
using HyPlayer.Platform.Runtime.Background;
using HyPlayer.Platform.Xaml;
using HyPlayer.Shell.Input;
using HyPlayer.Shell.Login;
using HyPlayer.Shell.Navigation;
using HyPlayer.Shell.Navigation.Services;
using HyPlayer.Shell.Playback;
using HyPlayer.Shell.Services;
using HyPlayer.UI.TeachingTips;
using TeachingTip = Microsoft.UI.Xaml.Controls.TeachingTip;

#endregion


// https://go.microsoft.com/fwlink/?LinkId=402352&clcid=0x804 上介绍了"空白页"项模板

namespace HyPlayer.Shell;

/// <summary>
///     Root shell page: title bar, navigation frame, and global input.
///     Account, login, and search logic are delegated to shell components.
/// </summary>
public sealed partial class BasePage : Page
{
    private readonly ShellLoginService _loginService = Ioc.Default.GetRequiredService<ShellLoginService>();

    private readonly NavigationShellViewModel _navigationShell =
        Ioc.Default.GetRequiredService<NavigationShellViewModel>();

    private readonly IAppNavigator _navigator = Ioc.Default.GetRequiredService<IAppNavigator>();
    private readonly AppearanceCustomizer _appearance = Ioc.Default.GetRequiredService<AppearanceCustomizer>();
    private readonly IGamepadShortcutService _gamepad =
        Ioc.Default.GetRequiredService<IGamepadShortcutService>();
    private readonly IKeyboardShortcutService _keyboardShortcuts =
        Ioc.Default.GetRequiredService<IKeyboardShortcutService>();
    private readonly IPlaybackControlService _playback = Ioc.Default.GetRequiredService<IPlaybackControlService>();
    private readonly HyPlayer.Domain.Settings.UISettings _setting =
        Ioc.Default.GetRequiredService<HyPlayer.Domain.Settings.UISettings>();
    private readonly IShellHostStateService _shellHost = Ioc.Default.GetRequiredService<IShellHostStateService>();
    private readonly PlaybackSurfaceStore _surfaceStore = Ioc.Default.GetRequiredService<PlaybackSurfaceStore>();
    private readonly IBackgroundTaskRunner _taskRunner = Ioc.Default.GetRequiredService<IBackgroundTaskRunner>();
    private readonly ITeachingTipService _teachingTip = Ioc.Default.GetRequiredService<ITeachingTipService>();

    public BasePage()
    {
        InitializeComponent();
        _shellHost.AppTitleBar = AppTitleBar;
        _teachingTip.Tip = TheTeachingTip;

        _taskRunner.Forget(_playback.InitializeAsync(), "initialize playback control");

        ApplicationView.TerminateAppOnFinalViewClose = false;
        _navigator.AttachNavigationView(NavMain, BaseFrame, _navigationShell);
        _navigationShell.UpdateAccountStatus();
        _gamepad.Attach(Window.Current.CoreWindow);
        _keyboardShortcuts.Attach(Window.Current.CoreWindow);
        Window.Current.CoreWindow.KeyDown += CoreWindow_KeyDown;
        Window.Current.CoreWindow.PointerPressed += CoreWindow_PointerPressed;
        _setting.PropertyChanged += OnAppearanceSettingChanged;
        _appearance.ApplyBackground(CustomBackgroundImage, CustomBackgroundBlur, CustomBackgroundBlurBrush);
    }

    private void OnAppearanceSettingChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(HyPlayer.Domain.Settings.UISettings.CustomUiFontFamily):
                _appearance.ApplyFont();
                break;
            case nameof(HyPlayer.Domain.Settings.UISettings.BackgroundImagePath):
            case nameof(HyPlayer.Domain.Settings.UISettings.BackgroundImageOpacity):
            case nameof(HyPlayer.Domain.Settings.UISettings.BackgroundImageBlur):
            case nameof(HyPlayer.Domain.Settings.UISettings.BackgroundImageStretch):
                _appearance.ApplyBackground(CustomBackgroundImage, CustomBackgroundBlur, CustomBackgroundBlurBrush);
                break;
        }
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        Bindings.StopTracking();
        _navigator.DetachNavigationView(NavMain);
        _gamepad.Detach(Window.Current.CoreWindow);
        _keyboardShortcuts.Detach(Window.Current.CoreWindow);
        Window.Current.CoreWindow.KeyDown -= CoreWindow_KeyDown;
        Window.Current.CoreWindow.PointerPressed -= CoreWindow_PointerPressed;
        _setting.PropertyChanged -= OnAppearanceSettingChanged;
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
        if (args.VirtualKey == VirtualKey.Escape)
        {
            if (_surfaceStore.IsExpanded && _surfaceStore.RequestEscape()) return;
            CollapseExpandedPlayerIfNeeded();
        }
    }

    private Brush? GetWindowBackground(bool acrylic)
    {
        if (!acrylic) return null;
        if (!_setting.CustomAcrylic)
            return Windows.UI.Xaml.Application.Current.Resources["NormalWindowBackgroundAcrylic"] as Brush;
        var tintColor = (Windows.UI.Color)Windows.UI.Xaml.Application.Current.Resources["SystemRevealAltHighColor"];
        return new AcrylicBrush
        {
            BackgroundSource = AcrylicBackgroundSource.HostBackdrop,
            TintColor = tintColor,
            TintOpacity = _setting.CustomTintOpacity,
            TintLuminosityOpacity = _setting.CustomTintLuminosityOpacity,
            FallbackColor = tintColor
        };
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
            dialog.PrimaryButtonClick += (_, _) => _setting.DisablePopUp = true;
            _ = dialog.ShowAsync();
        }

        _ = UpdateManager.PopupVersionCheck(true);
        _taskRunner.Forget(FolderAccessGuard.CheckSavedFoldersAsync(), "check saved folder access");
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
