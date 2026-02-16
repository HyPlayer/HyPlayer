using HyPlayer.LyricRenderer.Abstraction;
using HyPlayer.LyricRenderer.Abstraction.Render;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Text;
using Polly.Caching;
using System;
using Windows.Foundation;
using Windows.UI;
using Windows.UI.Text;
using Windows.UI.Xaml;

namespace HyPlayer.LyricRenderer.LyricLineRenderers;

public class ActionLyricLine : RenderingLyricLine
{
    private long _lastReactionTime;
    private ReactionState _reactionState;
    private float _canvasWidth;
    private CanvasTextFormat textFormat;
    private CanvasTextLayout textLayout;
    private bool _sizeChanged;
    private float _canvasHeight;

    private CanvasRenderTarget _staticPersistCache = null;

    public string Text { get; set; }
    public string ActionUri { get; set; }


    public override void GoToReactionState(ReactionState state, RenderContext context)
    {
        _lastReactionTime = context.CurrentLyricTime;
        _reactionState = state;
    }

    public override bool Render(CanvasDrawingSession session, LineRenderOffset offset, RenderContext context)
    {
        float actualOffsetX = offset.X - (float)(textLayout?.LayoutBounds.Left ?? 0);

        if (_reactionState == ReactionState.Enter)
        {

            var color = new Color
            {
                A = 40,
                R = 135,
                G = 206,
                B = 255
            };
            session.FillRoundedRectangle(0, offset.Y,
                RenderingWidth + 32, RenderingHeight, 6, 6, color);
        }
        actualOffsetX += 16;
        var drawingTop = offset.Y;
        session.DrawImage(_staticPersistCache, actualOffsetX, drawingTop);
        return true;
    }

    public override void OnKeyFrame(CanvasDrawingSession session, RenderContext context)
    {
        if (_canvasWidth == 0.0f) return;
        if (textFormat is null)
            OnTypographyChanged(session, context);
    }

    public override void OnRenderSizeChanged(CanvasDrawingSession session, RenderContext context)
    {
        _sizeChanged = true;
        _canvasWidth = context.ItemWidth;
        _canvasHeight = context.ViewHeight;
        OnTypographyChanged(session, context);
    }

    public override void OnTypographyChanged(CanvasDrawingSession session, RenderContext context)
    {
        textFormat = new CanvasTextFormat
        {
            FontSize = TypographySelector(t => t?.LyricFontSize, context)!.Value / 2,
            HorizontalAlignment =
                TypographySelector(t => t?.Alignment, context)!.Value switch
                {
                    TextAlignment.Right => CanvasHorizontalAlignment.Right,
                    TextAlignment.Center => CanvasHorizontalAlignment.Center,
                    _ => CanvasHorizontalAlignment.Left
                },
            VerticalAlignment = CanvasVerticalAlignment.Top,
            WordWrapping = CanvasWordWrapping.Wrap,
            Direction = CanvasTextDirection.LeftToRightThenTopToBottom,
            FontFamily = TypographySelector(t => t?.Font, context),
            FontWeight = FontWeights.Normal
        };

        if (textLayout is null || _sizeChanged)
        {
            _sizeChanged = false;
            textLayout = new CanvasTextLayout(session, Text, textFormat,
                Math.Clamp(context.ItemWidth - 16, 0, int.MaxValue), _canvasHeight);
        }

        _staticPersistCache?.Dispose();
        _staticPersistCache = new CanvasRenderTarget(session, RenderingWidth, RenderingHeight, context.Dpi);

        using (var pstDs = _staticPersistCache.CreateDrawingSession())
        {
            pstDs.DrawTextLayout(textLayout, 0, 0, TypographySelector(t => t?.IdleColor, context)!.Value);
        }

        RenderingHeight = (float)(textLayout?.LayoutBounds.Height ?? 0);
        RenderingWidth = (float)(textLayout?.LayoutBounds.Width ?? 0);
    }
}