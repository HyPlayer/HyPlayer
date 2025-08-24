using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Effects;
using System.Numerics;
using Windows.Foundation;
using Windows.UI;

namespace HyPlayer.LyricRenderer.Builder;

public class CanvasImageBuilder(ICanvasImage source)
{
    private ICanvasImage _source = source;

    public CanvasImageBuilder AddOpacityEffect(float opacity)
    {
        _source = new OpacityEffect
        {
            Source = _source,
            Opacity = opacity,
        };
        return this;
    }

    public CanvasImageBuilder AddTransform2DEffect(Matrix3x2 transformMatrix)
    {
        _source = new Transform2DEffect
        {
            Source = _source,
            TransformMatrix = transformMatrix,
        };
        return this;
    }

    public CanvasImageBuilder AddGaussianBlurEffect(float blurAmount)
    {
        _source = new GaussianBlurEffect
        {
            Source = _source,
            BlurAmount = blurAmount,
        };
        return this;
    }

    public CanvasImageBuilder AddShadowEffect(float blurAmount, Color color = default)
    {
        _source = new ShadowEffect
        {
            Source = _source,
            BlurAmount = blurAmount,
            ShadowColor = color,
        };
        return this;
    }

    public CanvasImageBuilder AddCropEffect(Rect sourceRectangle, EffectBorderMode borderMode = EffectBorderMode.Soft)
    {
        _source = new CropEffect
        {
            Source = _source,
            SourceRectangle = sourceRectangle,
            BorderMode = borderMode
        };
        return this;
    }

    public CanvasImageBuilder AddVignetteEffect(Color color, float amount, float curve = 1f)
    {
        _source = new VignetteEffect
        {
            Source = _source,
            Amount = amount,
            Curve = curve,
            Color = color
        };
        return this;
    }
    public ICanvasImage Build()
    {
        return _source;
    }
}