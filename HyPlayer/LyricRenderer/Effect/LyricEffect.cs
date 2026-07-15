using HyPlayer.LyricRenderer.Abstraction;
using HyPlayer.LyricRenderer.Abstraction.Render;
using HyPlayer.LyricRenderer.Animator;
using HyPlayer.LyricRenderer.Animator.EaseFunctions;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Effects;
using System;
using Windows.UI.Xaml.Media.Animation;

namespace HyPlayer.LyricRenderer.Effect;

public abstract class LyricEffect : IDisposable
{
    public virtual TimeSpan Duration { get; init; } = TimeSpan.FromSeconds(0.5);
    public virtual EaseFunctionBase Easing { get; init; } = new CustomCircleEase
    {
        EasingMode = EasingMode.EaseOut
    };

    protected ICanvasEffect Effect { get; init; } = null!;

    public abstract ICanvasImage Apply(ICanvasImage source, RenderingLyricLine lyricLine, RenderContext context);

    public void Dispose()
    {
        Effect.Dispose();
    }
}

public abstract class LyricEffect<T> : LyricEffect where T : ICanvasEffect
{
    protected new abstract T Effect { get; }
    protected LyricEffect() : base()
    {
        base.Effect = Effect;
    }
}