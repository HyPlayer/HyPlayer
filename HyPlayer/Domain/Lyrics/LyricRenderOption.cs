using Windows.UI;
using Windows.UI.Text;
using Microsoft.Graphics.Canvas.Text;
using FontStyle = Windows.UI.Text.FontStyle;

namespace HyPlayer.Domain.Lyrics;

public struct LyricRenderOption
{
    public float FontSize { get; set; }
    public CanvasHorizontalAlignment HorizontalAlignment { get; set; }
    public CanvasVerticalAlignment VerticalAlignment { get; set; }
    public FontStyle FontStyle { get; set; }
    public FontWeight FontWeight { get; set; }
    public string FontFamily { get; set; }
    public float BlurAmount { get; set; }
    public EaseFunctionBase EaseFunction { get; set; }

    public Color HighlightColor { get; set; }
    public Color LyricIdleColor { get; set; }
    public Color ShadowColor { get; set; }
}
