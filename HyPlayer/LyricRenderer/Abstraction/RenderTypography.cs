#nullable enable
using Windows.UI;
using Windows.UI.Text;
using Windows.UI.Xaml;

namespace HyPlayer.LyricRenderer.Abstraction;

public class RenderTypography
{
    public TextAlignment? Alignment { get; set; } = null;
    public Color? IdleColor { get; set; } = null;
    public Color? FocusingColor { get; set; } = null;
    public float? LyricFontSize { get; set; } = null;
    public float? TranslationFontSize { get; set; } = null;
    public float? TransliterationFontSize { get; set; } = null;
    public FontWeight? FontWeight { get; set; } = null;
    public FontStyle? FontStyle { get; set; } = null;
    public string? Font { get; set; } = null;

    public static RenderTypography Default = new()
    {
        Alignment = TextAlignment.Center,
        IdleColor = Colors.White,
        FocusingColor = Colors.Yellow,
        LyricFontSize = 24,
        TranslationFontSize = 16,
        FontWeight = FontWeights.Normal,
        FontStyle = Windows.UI.Text.FontStyle.Normal,
        Font = "Microsoft YaHei"
    };
}