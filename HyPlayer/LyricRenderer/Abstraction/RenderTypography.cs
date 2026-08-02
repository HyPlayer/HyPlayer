#nullable enable
using Windows.UI;
using Windows.UI.Text;
using Windows.UI.Xaml;

namespace HyPlayer.LyricRenderer.Abstraction;

public class RenderTypography
{
    public static RenderTypography Default { get; } = new()
    {
        Alignment = TextAlignment.Center,
        IdleColor = Colors.White,
        ShadowColor = Colors.Black,
        FocusingColor = Colors.Yellow,
        LyricFontSize = 24,
        TranslationFontSize = 16,
        FontWeight = FontWeights.Normal,
        FontStyle = Windows.UI.Text.FontStyle.Normal,
        Font = "Microsoft YaHei UI"
    };

    public TextAlignment? Alignment { get; set; }
    public Color? IdleColor { get; set; }
    public Color? FocusingColor { get; set; }
    public Color? ShadowColor { get; set; }
    public float? LyricFontSize { get; set; }
    public float? TranslationFontSize { get; set; }
    public float? TransliterationFontSize { get; set; } = null;
    public FontWeight? FontWeight { get; set; }
    public FontStyle? FontStyle { get; set; }
    public string? Font { get; set; }
}
