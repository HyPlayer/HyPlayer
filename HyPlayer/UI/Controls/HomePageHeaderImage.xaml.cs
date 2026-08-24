using System;
using System.Net.Http;
using System.Numerics;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.System;
using Windows.UI;
using Windows.UI.Composition;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Hosting;
using Windows.UI.Xaml.Media.Animation;
using Windows.UI.Xaml.Media.Imaging;
using CommunityToolkit.Mvvm.DependencyInjection;
using CommunityToolkit.WinUI.Animations;
using Microsoft.Graphics.Canvas.Effects;
using CommunityToolkit.WinUI;
using HyPlayer.Domain.Settings;

namespace HyPlayer.UI.Controls;
#nullable enable

public sealed partial class HomePageHeaderImage : UserControl
{
    private const string BingBaseUrl = "https://www.bing.com";
    private const string BingDailyImageEndpoint =
        BingBaseUrl + "/HPImageArchive.aspx?format=js&idx=0&n=1&mkt=zh-CN";
    private const string CachedImageUrlKey = "Home.BingDailyImage.Url";
    private const string CachedCopyrightKey = "Home.BingDailyImage.Copyright";
    private const string CachedCopyrightUrlKey = "Home.BingDailyImage.CopyrightUrl";
    private const string GradientSizeKey = "GradientSize";
    private ExpressionAnimation? _bottomGradientStartPointAnimation;
    private Compositor? _compositor;
    private CompositionLinearGradientBrush? _imageGridBottomGradientBrush;
    private CompositionEffectBrush? _imageGridEffectBrush;
    private ExpressionAnimation? _imageGridSizeAnimation;
    private SpriteVisual? _imageGridSpriteVisual;
    private CompositionSurfaceBrush? _imageGridSurfaceBrush;
    private Visual? _imageGridVisual;
    private CompositionVisualSurface? _imageGridVisualSurface;
    private readonly HttpClient _httpClient = Ioc.Default.GetRequiredService<HttpClient>();
    private readonly UISettings _uiSettings = Ioc.Default.GetRequiredService<UISettings>();
    private CancellationTokenSource? _imageLoadCancellation;
    private Uri? _copyrightUri;

    public HomePageHeaderImage()
    {
        this.InitializeComponent();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _imageGridVisual = ElementCompositionPreview.GetElementVisual(ImageGrid);
        _compositor = _imageGridVisual.Compositor;

        _imageGridSizeAnimation = _compositor.CreateExpressionAnimation("Visual.Size");
        _imageGridSizeAnimation.SetReferenceParameter("Visual", _imageGridVisual);

        _imageGridVisualSurface = _compositor.CreateVisualSurface();
        _imageGridVisualSurface.SourceVisual = _imageGridVisual;
        _imageGridVisualSurface.StartAnimation(nameof(CompositionVisualSurface.SourceSize), _imageGridSizeAnimation);

        _imageGridSurfaceBrush = _compositor.CreateSurfaceBrush(_imageGridVisualSurface);
        _imageGridSurfaceBrush.Stretch = CompositionStretch.UniformToFill;

        _bottomGradientStartPointAnimation = CreateExpressionAnimation(
            nameof(CompositionLinearGradientBrush.StartPoint), $"Vector2(0.5, Visual.Size.Y - this.{GradientSizeKey})");
        SetBottomGradientStartPoint();

        _imageGridBottomGradientBrush = _compositor.CreateLinearGradientBrush();
        _imageGridBottomGradientBrush.MappingMode = CompositionMappingMode.Absolute;
        if (_bottomGradientStartPointAnimation is not null)
            _imageGridBottomGradientBrush.StartAnimation(_bottomGradientStartPointAnimation);
        var animation = CreateExpressionAnimation(nameof(CompositionLinearGradientBrush.EndPoint),
            "Vector2(0.5, Visual.Size.Y)");
        if (animation is not null) _imageGridBottomGradientBrush.StartAnimation(animation);
        _imageGridBottomGradientBrush.CreateColorStopsWithEasingFunction(EasingType.Sine, EasingMode.EaseInOut, 0f, 1f);
        var alphaMask = new AlphaMaskEffect
        {
            Source = new CompositionEffectSourceParameter("ImageGrid"),
            AlphaMask = new CompositionEffectSourceParameter("Gradient")
        };

        var effectFactory = _compositor.CreateEffectFactory(alphaMask);
        _imageGridEffectBrush = effectFactory.CreateBrush();
        _imageGridEffectBrush.SetSourceParameter("ImageGrid", _imageGridSurfaceBrush);
        _imageGridEffectBrush.SetSourceParameter("Gradient", _imageGridBottomGradientBrush);

        _imageGridSpriteVisual = _compositor.CreateSpriteVisual();
        _imageGridSpriteVisual.RelativeSizeAdjustment = Vector2.One;
        _imageGridSpriteVisual.Brush = _imageGridEffectBrush;

        ElementCompositionPreview.GetElementVisual(ImageGridSurfaceRec).ParentForTransform = _imageGridVisual;

        ElementCompositionPreview.SetElementChildVisual(ImageGridSurfaceRec, _imageGridSpriteVisual);

        _imageLoadCancellation?.Cancel();
        _imageLoadCancellation?.Dispose();
        _imageLoadCancellation = new CancellationTokenSource();
        _ = LoadBingDailyImageAsync(_imageLoadCancellation.Token);
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _imageLoadCancellation?.Cancel();
        _imageLoadCancellation?.Dispose();
        _imageLoadCancellation = null;
        ElementCompositionPreview.SetElementChildVisual(ImageGridSurfaceRec, null);
        _imageGridSpriteVisual?.Dispose();
        _imageGridEffectBrush?.Dispose();
        _imageGridSurfaceBrush?.Dispose();
        _imageGridVisualSurface?.Dispose();
        _imageGridBottomGradientBrush?.Dispose();
        _imageGridSizeAnimation?.Dispose();
        _bottomGradientStartPointAnimation?.Dispose();
        _bottomGradientStartPointAnimation = null;
        _imageGridBottomGradientBrush = null;
    }

    private async Task LoadBingDailyImageAsync(CancellationToken cancellationToken)
    {
        if (_uiSettings.NoImage)
        {
            HeroImage.Source = null;
            HeroOverlayImage.Source = null;
            CopyrightInfoButton.Visibility = Visibility.Collapsed;
            return;
        }

        BingDailyImage? dailyImage = null;
        try
        {
            using var response = await _httpClient.GetAsync(BingDailyImageEndpoint, cancellationToken);
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            dailyImage = ParseBingDailyImage(json);
            if (dailyImage is not null)
                CacheDailyImage(dailyImage);
            else
                dailyImage = GetCachedDailyImage();
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception)
        {
            dailyImage = GetCachedDailyImage();
        }

        if (cancellationToken.IsCancellationRequested || dailyImage is null)
            return;

        HeroImage.Source = new BitmapImage(dailyImage.ImageUri);
        HeroOverlayImage.Source = new BitmapImage(dailyImage.ImageUri);
        ToolTipService.SetToolTip(CopyrightInfoButton, $"Bing 每日一图 · {dailyImage.Copyright}");
        _copyrightUri = dailyImage.CopyrightUri;
        CopyrightInfoButton.Visibility = Visibility.Visible;
    }

    private static BingDailyImage? ParseBingDailyImage(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("images", out var images) ||
            images.ValueKind != JsonValueKind.Array || images.GetArrayLength() == 0)
            return null;

        var image = images[0];
        if (!image.TryGetProperty("url", out var urlElement))
            return null;

        var imageUri = ResolveBingUri(urlElement.GetString());
        if (imageUri is null)
            return null;

        var copyright = image.TryGetProperty("copyright", out var copyrightElement)
            ? copyrightElement.GetString()
            : null;
        var copyrightUri = image.TryGetProperty("copyrightlink", out var copyrightLinkElement)
            ? ResolveBingUri(copyrightLinkElement.GetString())
            : null;

        return new BingDailyImage(imageUri,
            string.IsNullOrWhiteSpace(copyright) ? "图片版权信息由 Bing 提供" : copyright,
            copyrightUri);
    }

    private static Uri? ResolveBingUri(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        if (Uri.TryCreate(value, UriKind.Absolute, out var absoluteUri))
            return absoluteUri;

        return Uri.TryCreate(new Uri(BingBaseUrl), value, out var resolvedUri) ? resolvedUri : null;
    }

    private static void CacheDailyImage(BingDailyImage dailyImage)
    {
        var values = ApplicationData.Current.LocalSettings.Values;
        values[CachedImageUrlKey] = dailyImage.ImageUri.AbsoluteUri;
        values[CachedCopyrightKey] = dailyImage.Copyright;
        values[CachedCopyrightUrlKey] = dailyImage.CopyrightUri?.AbsoluteUri ?? string.Empty;
    }

    private static BingDailyImage? GetCachedDailyImage()
    {
        var values = ApplicationData.Current.LocalSettings.Values;
        var imageUri = ResolveBingUri(values[CachedImageUrlKey] as string);
        if (imageUri is null)
            return null;

        var copyright = values[CachedCopyrightKey] as string;
        var copyrightUri = ResolveBingUri(values[CachedCopyrightUrlKey] as string);
        return new BingDailyImage(imageUri,
            string.IsNullOrWhiteSpace(copyright) ? "图片版权信息由 Bing 提供" : copyright,
            copyrightUri);
    }

    private async void OnCopyrightClick(object sender, RoutedEventArgs e)
    {
        if (_copyrightUri is not null)
            await Launcher.LaunchUriAsync(_copyrightUri);
    }

    private void OnLoading(FrameworkElement sender, object args)
    {
        if (HeroImage.Source == null)
            HeroImage.GetVisual().Opacity = 0;
        else
            AnimateImage();
    }

    private void SetBottomGradientStartPoint()
    {
        _bottomGradientStartPointAnimation?.Properties.InsertScalar(GradientSizeKey, 120);
    }

    private void OnImageOpened(object sender, RoutedEventArgs e)
    {
        AnimateImage();
    }

    private void AnimateImage()
    {
        AnimationBuilder.Create()
            .Opacity(1, 0, duration: TimeSpan.FromMilliseconds(300), easingMode: EasingMode.EaseOut)
            .Scale(1, 1.1f, duration: TimeSpan.FromMilliseconds(400), easingMode: EasingMode.EaseOut)
            .Start(HeroImage);

        AnimationBuilder.Create()
            .Opacity(0.5, 0, duration: TimeSpan.FromMilliseconds(300), easingMode: EasingMode.EaseOut)
            .Scale(1, 1.1f, duration: TimeSpan.FromMilliseconds(400), easingMode: EasingMode.EaseOut)
            .Start(HeroOverlayImage);
    }


    private ExpressionAnimation? CreateExpressionAnimation(string target, string expression)
    {
        var ani = _compositor?.CreateExpressionAnimation(expression);
        if (ani != null)
        {
            ani.SetReferenceParameter("Visual", _imageGridVisual);
            ani.Target = target;
        }

        return ani;
    }

    private sealed record BingDailyImage(Uri ImageUri, string Copyright, Uri? CopyrightUri);
}
