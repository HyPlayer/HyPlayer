using System.Collections.Generic;
using Windows.UI;
using Windows.UI.Xaml;
using Microsoft.Graphics.Canvas;

namespace HyPlayer.LyricRenderer.Abstraction.Render;

public abstract class RenderingLyricLine
{
    public int Id { get; set; }

    public double RenderingHeight { get; set; }
    public double RenderingWidth { get; set; }

    public Color IdleColor { get; set; } = Colors.Gray;

    public Color FocusingColor { get; set; } = Colors.Yellow;

    public double LyricFontSize { get; set; } = 48;

    public double TranslationFontSize { get; set; } = 24;
    
    public double TransliterationFontSize { get; set; } = 24;

    public bool Hidden { get; set; }

    public List<long> KeyFrames { get; set; }

    public long StartTime { get; set; }
    public long EndTime { get; set; }
    public TextAlignment TextAlignment { get; set; } = TextAlignment.Left;

    public abstract void GoToReactionState(ReactionState state, long time);
    public abstract bool Render(CanvasDrawingSession session, LineRenderOffset offset, long currentLyricTime);
    public abstract void OnKeyFrame(CanvasDrawingSession session,long time);
    public abstract void OnRenderSizeChanged(CanvasDrawingSession session, double width, double height, long time);
    public abstract void OnTypographyChanged(CanvasDrawingSession session);
}

public enum ReactionState
{
    Leave,
    Enter,
    Press
}