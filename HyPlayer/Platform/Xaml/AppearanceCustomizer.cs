using System;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;
using Windows.UI.Xaml.Shapes;
using CommunityToolkit.WinUI.Media;
using HyPlayer.Domain.Settings;

namespace HyPlayer.Platform.Xaml;

public sealed class AppearanceCustomizer
{
    private const string FontResourceKey = "ContentControlThemeFontFamily";
    private static readonly string[] BackgroundFileTypes = [".jpg", ".jpeg", ".png", ".bmp", ".gif", ".webp"];

    private readonly UISettings _settings;
    private object? _defaultFont;

    public AppearanceCustomizer(UISettings settings)
    {
        _settings = settings;
    }

    public void ApplyFont()
    {
        var name = _settings.CustomUiFontFamily;
        var resources = Windows.UI.Xaml.Application.Current.Resources;
        if (_defaultFont is null && resources.TryGetValue(FontResourceKey, out var original))
            _defaultFont = original;

        if (string.IsNullOrWhiteSpace(name))
        {
            if (_defaultFont is not null)
                resources[FontResourceKey] = _defaultFont;
            else
                resources.Remove(FontResourceKey);
            (Window.Current?.Content as Control)?.ClearValue(Control.FontFamilyProperty);
            return;
        }

        var font = new FontFamily(name);
        resources[FontResourceKey] = font;
        if (Window.Current?.Content is Control root)
            root.FontFamily = font;
    }

    public void ApplyBackground(Image image, Rectangle blur, BackdropBlurBrush blurBrush)
    {
        var file = _settings.BackgroundImagePath;
        if (string.IsNullOrEmpty(file))
        {
            image.Source = null;
            image.Visibility = Visibility.Collapsed;
            blur.Visibility = Visibility.Collapsed;
            return;
        }

        if (image.Tag as string != file || image.Source is null)
        {
            image.Tag = file;
            image.Source = new BitmapImage(new Uri("ms-appdata:///local/" + file))
            {
                DecodePixelType = DecodePixelType.Logical,
                DecodePixelWidth = 1920
            };
        }

        image.Opacity = Math.Clamp(_settings.BackgroundImageOpacity, 0, 100) / 100;
        image.Stretch = _settings.BackgroundImageStretch switch
        {
            0 => Stretch.UniformToFill,
            1 => Stretch.Uniform,
            2 => Stretch.Fill,
            _ => Stretch.None
        };
        image.Visibility = Visibility.Visible;
        blurBrush.Amount = Math.Clamp(_settings.BackgroundImageBlur, 0, 60);
        blur.Visibility = _settings.BackgroundImageBlur > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    public async Task<bool> PickBackgroundAsync()
    {
        var picker = new FileOpenPicker
        {
            SuggestedStartLocation = PickerLocationId.PicturesLibrary,
            ViewMode = PickerViewMode.Thumbnail
        };
        foreach (var fileType in BackgroundFileTypes)
            picker.FileTypeFilter.Add(fileType);

        var picked = await picker.PickSingleFileAsync();
        if (picked is null) return false;

        var name = $"ui_background_{DateTime.Now.Ticks}{picked.FileType.ToLowerInvariant()}";
        await picked.CopyAsync(ApplicationData.Current.LocalFolder, name, NameCollisionOption.ReplaceExisting);
        var previous = _settings.BackgroundImagePath;
        _settings.BackgroundImagePath = name;
        await DeleteLocalFileAsync(previous);
        return true;
    }

    public async Task RemoveBackgroundAsync()
    {
        var previous = _settings.BackgroundImagePath;
        _settings.BackgroundImagePath = string.Empty;
        await DeleteLocalFileAsync(previous);
    }

    private static async Task DeleteLocalFileAsync(string? name)
    {
        if (string.IsNullOrEmpty(name)) return;

        try
        {
            if (await ApplicationData.Current.LocalFolder.TryGetItemAsync(name) is StorageFile old)
                await old.DeleteAsync(StorageDeleteOption.PermanentDelete);
        }
        catch
        {
        }
    }
}
