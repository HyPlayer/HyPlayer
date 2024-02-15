using Windows.UI;
using Windows.UI.Text;
using Windows.UI.Xaml;

namespace HyPlayer.LyricRenderer.Abstraction;

public class RenderTypography
{
    public TextAlignment Alignment { get; set; }
    public Color IdleColor { get; set; } = Colors.Gray;
    public Color FocusingColor { get; set; } = Colors.Yellow;
    public float LyricFontSize { get; set; } = 48;
    public float TranslationFontSize { get; set; } = 24;
    public float TransliterationFontSize { get; set; } = 24;
    public FontWeight FontWeight { get; set; } = FontWeights.Normal;
    public FontStyle FontStyle { get; set; } = FontStyle.Normal;
}