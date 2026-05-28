using CommunityToolkit.Mvvm.DependencyInjection;
using CommunityToolkit.WinUI.Media;
using CommunityToolkit.WinUI.Helpers;
using HyPlayer.Domain.Settings;
using HyPlayer.Services.Abstractions;
using HyPlayer.Services.Playback;
using HyPlayer.Shell.CompactPlayer;
using System;
using System.ComponentModel;
using Windows.UI;
using Windows.UI.WindowManagement;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;
using WinRT;
using Windows.UI.Xaml.Media.Imaging;

namespace HyPlayer.Shell;

public sealed partial class CompactPlayerPage : Page
{
    public static readonly DependencyProperty ControlHoverProperty = DependencyProperty.Register(
        "ControlHover", typeof(Brush), typeof(CompactPlayerPage),
        new PropertyMetadata(new SolidColorBrush(Colors.Transparent)));

    private readonly Setting _setting = Ioc.Default.GetRequiredService<Setting>();
    private readonly PlaybackStateService _state = Ioc.Default.GetRequiredService<PlaybackStateService>();
    private readonly SolidColorBrush _transparentBrush = new(Colors.Transparent);

    public CompactPlayerViewModel ViewModel { get; } = Ioc.Default.GetRequiredService<CompactPlayerViewModel>();

    public CompactPlayerPage()
    {
        InitializeComponent();
    }

    public Brush ControlHover
    {
        get => (Brush)GetValue(ControlHoverProperty);
        set => SetValue(ControlHoverProperty, value);
    }

    private void CompactPlayerPage_Unloaded(object sender, RoutedEventArgs e)
    {
        ViewModel.Detach();
    }

    internal void OnPlaybarVisibilityChanged(bool isActivated)
    {
        if (isActivated)
        {
            PointerOutAni.SkipToFill();
            ControlHover = new BackdropBlurBrush { Amount = 10.0 };
            PointerInAni.Begin();
        }
        else
        {
            PointerInAni.SkipToFill();
            if (!_setting.CompactPlayerPageBlurStatus)
                ControlHover = _transparentBrush;
            PointerOutAni.Begin();
        }
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        ViewModel.SyncFromState();
        (e.Parameter?.As<AppWindow>()).TitleBar.ExtendsContentIntoTitleBar = true;
    }

    private void OnRightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        if (ViewModel.ToggleCompactBlurCommand.CanExecute(null))
            ViewModel.ToggleCompactBlurCommand.Execute(null);
    }

    private void Grid_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        OnPlaybarVisibilityChanged(true);
    }

    private void Grid_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        OnPlaybarVisibilityChanged(false);
    }
    private void RunOnUIThread(Action action)
    {
        _ = Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () => { action(); });
    }
}
