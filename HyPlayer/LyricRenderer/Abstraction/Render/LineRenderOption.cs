using System.Drawing;
using Windows.UI.Text;
using Windows.UI.Xaml;
using FontStyle = Windows.UI.Text.FontStyle;

namespace HyPlayer.LyricRenderer.Abstraction.Render;

public class LineRenderOption
{
    public TextAlignment TextAlignment { get; set; } = TextAlignment.Left;
    public FontWeight FontWeight { get; set; } = FontWeights.Normal;
    public FontStyle FontStyle { get; set; } = FontStyle.Normal;
    public Color? FontForegroundColor { get; set; }
    public Color? FontBackgroundColor { get; set; }
    public int? BeatPerMinute { get; set; }
    public bool Display { get; set; } = true;
}