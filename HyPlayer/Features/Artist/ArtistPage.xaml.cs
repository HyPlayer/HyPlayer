#region

using System;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;
using HyPlayer.Platform.Runtime.Background;
using CommunityToolkit.Mvvm.DependencyInjection;
using HyPlayer.Application.Notifications;

#endregion

// https://go.microsoft.com/fwlink/?LinkId=234238 上介绍了“空白页”项模板

namespace HyPlayer.Features.Artist;

/// <summary>
///     可用于自身或导航至 Frame 内部的空白页。
/// </summary>
public sealed partial class ArtistPage : Page
{
    private readonly INotificationService _notification = Ioc.Default.GetRequiredService<INotificationService>();
    private readonly IBackgroundTaskRunner _taskRunner =
        Ioc.Default.GetRequiredService<IBackgroundTaskRunner>();

    public ArtistPageViewModel ViewModel { get; } = Ioc.Default.GetRequiredService<ArtistPageViewModel>();

    public ArtistPage()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        var artistId = e.Parameter as string;
        if (artistId is null)
        {
            _notification.ShowMessage("艺人ID为空", "请检查传入的参数是否正确");
            return;
        }

        _taskRunner.Forget(ViewModel.LoadAsync(artistId), "load artist page");
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        HotSongContainer.ReleaseResources();
        AllSongContainer.ReleaseResources();
        AlbumContainer.ReleaseResources();
        Bindings.StopTracking();
    }

    private void OnArtistHeaderScrollProgressChanged(object? sender, EventArgs e)
    {
        var progress = ArtistPivotView.HeaderScrollProgress;
        GridPersonalInformation.Opacity = Math.Clamp(1 - progress * 1.4, 0, 1);
        RectangleImageBack.Opacity = Math.Clamp(1 - progress * 1.1, 0, 1);
        RectangleImageBackAcrylic.Opacity = Math.Clamp(1 - progress * 1.1, 0, 1);
        TextBlockDesc.Opacity = Math.Clamp(1 - progress * 0.8, 0, 1);

        UserScale.ScaleX = UserScale.ScaleY = Math.Clamp(1 - progress * 0.8, 0, 1);
        UserInfoScale.ScaleX = UserInfoScale.ScaleY = Math.Clamp(1 - progress * 0.6, 0, 1);
        DescScale.ScaleX = DescScale.ScaleY = Math.Clamp(1 - progress * 0.4, 0, 1);
    }
}
