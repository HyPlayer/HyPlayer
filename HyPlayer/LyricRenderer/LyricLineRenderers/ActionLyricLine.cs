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

    private ICanvasImage _staticPersistCache = null;

    public string Text { get; set; }
    public string ActionUri { get; set; }

    // 新增：用于记录文本排版的实际起始 X 坐标
    private float _renderStartX = 0f;

    public override void GoToReactionState(ReactionState state, RenderContext context)
    {
        _lastReactionTime = context.CurrentLyricTime;
        _reactionState = state;
    }

    public override bool Render(CanvasDrawingSession session, LineRenderOffset offset, RenderContext context)
    {
        if (textLayout is null) return true;

        float actualOffsetX = offset.X;

        if (_reactionState == ReactionState.Enter)
        {
            var color = new Color
            {
                A = 40,
                R = 135,
                G = 206,
                B = 255
            };
            session.FillRoundedRectangle(offset.X, offset.Y,
                RenderingWidth, RenderingHeight, 6, 6, color);
        }

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

            _renderStartX = (float)textLayout.LayoutBounds.X;
        }

        RenderingHeight = (float)(textLayout?.LayoutBounds.Height ?? 0);
        RenderingWidth = (float)(textLayout?.LayoutBounds.Width ?? 0) + 32; // 加上 32 作为左右各 16 的 Padding 留白

        _staticPersistCache?.Dispose();
        CanvasDrawingSession pstDs;
        if (!context.Effects.CacheRenderTarget)
        {
            var staticPersistCacheCCL = new CanvasCommandList(session);
            _staticPersistCache = staticPersistCacheCCL;
            pstDs = staticPersistCacheCCL.CreateDrawingSession();
        }
        else
        {
            var staticPersistCacheTarget = new CanvasRenderTarget(session, RenderingWidth, RenderingHeight, context.Dpi);
            _staticPersistCache = staticPersistCacheTarget;
            pstDs = staticPersistCacheTarget.CreateDrawingSession();
        }
        

        using (pstDs)
        {
            pstDs.Clear(Colors.Transparent);
            pstDs.DrawTextLayout(textLayout, -_renderStartX + 16, 0, TypographySelector(t => t?.IdleColor, context)!.Value);
        }
    }
}