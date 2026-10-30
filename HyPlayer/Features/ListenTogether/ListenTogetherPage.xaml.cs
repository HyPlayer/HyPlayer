using System;
using System.ComponentModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.DependencyInjection;
using HyPlayer.Platform.Runtime.Background;
using QRCoder;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage.Streams;
using Windows.UI.Text;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;

namespace HyPlayer.Features.ListenTogether;

public sealed partial class ListenTogetherPage : Page
{
    private readonly IBackgroundTaskRunner _taskRunner = Ioc.Default.GetRequiredService<IBackgroundTaskRunner>();
    private int _qrVersion;

    public ListenTogetherPage()
    {
        InitializeComponent();
    }

    public IListenTogetherService Service { get; } = Ioc.Default.GetRequiredService<IListenTogetherService>();

    public static string RoleText(bool isHost)
    {
        return isHost ? "你的身份：房主" : "你的身份：成员";
    }

    public static string ModeTitle(bool isMulti)
    {
        return isMulti ? "多人一起听" : "双人一起听";
    }

    public static string EndText(bool isMulti)
    {
        return isMulti ? "退出房间" : "结束一起听";
    }

    public static string RecommenderText(string name, bool isSystem, bool isPlaying)
    {
        var source = isSystem ? "系统推荐" : string.IsNullOrEmpty(name) ? string.Empty : $"{name} 推荐";
        return isPlaying ? string.IsNullOrEmpty(source) ? "正在播放" : $"正在播放 · {source}" : source;
    }

    public static FontWeight TitleWeight(bool isPlaying)
    {
        return isPlaying ? FontWeights.SemiBold : FontWeights.Normal;
    }

    public static ImageSource? Cover(string? url)
    {
        return Uri.TryCreate(url, UriKind.Absolute, out var uri)
            ? new BitmapImage(uri) { DecodePixelWidth = 88 }
            : null;
    }

    public static ImageSource? Avatar(string? url)
    {
        return Uri.TryCreate(url, UriKind.Absolute, out var uri)
            ? new BitmapImage(uri) { DecodePixelWidth = 112 }
            : null;
    }

    private void Page_Loaded(object sender, RoutedEventArgs e)
    {
        Service.PropertyChanged += Service_PropertyChanged;
        RefreshQr();
        if (Service.IsIdle) _taskRunner.Forget(Service.RefreshExistingRoomAsync(), "check existing listen together room");
    }

    private void Page_Unloaded(object sender, RoutedEventArgs e)
    {
        Service.PropertyChanged -= Service_PropertyChanged;
        _qrVersion++;
    }

    private void Service_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(IListenTogetherService.ShareLink)) RefreshQr();
    }

    private void RefreshQr()
    {
        var version = ++_qrVersion;
        var link = Service.ShareLink;
        if (string.IsNullOrEmpty(link))
        {
            QrImage.Source = null;
            return;
        }

        _taskRunner.Forget(ShowQrAsync(link, version), "render listen together qr code");
    }

    private async Task ShowQrAsync(string link, int version)
    {
        var bytes = await Task.Run(() =>
        {
            using var generator = new QRCodeGenerator();
            using var data = generator.CreateQrCode(link, QRCodeGenerator.ECCLevel.M);
            return new BitmapByteQRCode(data).GetGraphic(8);
        });
        if (version != _qrVersion) return;
        var image = new BitmapImage();
        using (var stream = new InMemoryRandomAccessStream())
        {
            using (var writer = new DataWriter(stream.GetOutputStreamAt(0)))
            {
                writer.WriteBytes(bytes);
                await writer.StoreAsync();
            }

            await image.SetSourceAsync(stream);
        }

        if (version == _qrVersion) QrImage.Source = image;
    }

    private void CreateRoom_Click(object sender, RoutedEventArgs e)
    {
        _taskRunner.Forget(Service.CreateRoomAsync(), "create listen together room");
    }

    private void CreateMultiRoom_Click(object sender, RoutedEventArgs e)
    {
        _taskRunner.Forget(Service.CreateMultiRoomAsync(), "create multi listen together room");
    }

    private void RequestSkip_Click(object sender, RoutedEventArgs e)
    {
        _taskRunner.Forget(Service.RequestSkipAsync(), "request skip in multi listen together room");
    }

    private void JoinRoom_Click(object sender, RoutedEventArgs e)
    {
        InviteError.Visibility = Visibility.Collapsed;
        _taskRunner.Forget(JoinAsync(InviteBox.Text), "join listen together room");
    }

    private async Task JoinAsync(string text)
    {
        if (await Service.ResolveInvitationAsync(text) is not { } invitation)
        {
            InviteError.Visibility = Visibility.Visible;
            return;
        }

        await Service.JoinRoomAsync(invitation);
    }

    private void TakeOver_Click(object sender, RoutedEventArgs e)
    {
        _taskRunner.Forget(Service.TakeOverAsync(), "take over listen together room");
    }

    private void EndExisting_Click(object sender, RoutedEventArgs e)
    {
        _taskRunner.Forget(ConfirmEndAsync(Service.EndExistingRoomAsync), "end existing listen together room");
    }

    private void Reconnect_Click(object sender, RoutedEventArgs e)
    {
        _taskRunner.Forget(Service.ReconnectAsync(), "reconnect listen together room");
    }

    private void CopyLink_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(Service.ShareLink)) return;
        var package = new DataPackage();
        package.SetText(Service.ShareLink);
        Clipboard.SetContent(package);
    }

    private void EndRoom_Click(object sender, RoutedEventArgs e)
    {
        _taskRunner.Forget(Service.IsMulti
            ? ConfirmEndAsync(Service.EndRoomAsync, "退出多人一起听？", "退出后房间里的其他人可以继续听。", "退出")
            : ConfirmEndAsync(Service.EndRoomAsync), "end listen together room");
    }

    private async Task ConfirmEndAsync(Func<Task> end, string title = "结束一起听？",
        string content = "结束后房间内所有成员都会退出。", string primary = "结束")
    {
        var dialog = new ContentDialog
        {
            Title = title,
            Content = content,
            PrimaryButtonText = primary,
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary) await end();
    }
}
