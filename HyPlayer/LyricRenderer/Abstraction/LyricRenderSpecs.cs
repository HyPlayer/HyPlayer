#nullable enable

using HyPlayer.Domain;

namespace HyPlayer.LyricRenderer.Abstraction;

public sealed class LyricRenderSpecs
{
    public static LyricRenderSpecs Modern { get; } = new()
    {
        MaxSelectedLines = 2,
        AnimationHeadstartMs = 120,
        LineDelayMs = 35,
        LineFinishProgressAnimationDurationMs = 280,
        TapProgressFreezeDurationMs = 100,
        ScaleAnimationDurationMs = 320,
        ScaleEnterAnimationDurationMs = 520,
        SyllableLift = 2,
        ShortTokenLiftDurationThresholdMs = 1000,
        CooperativeLiftWindow = 2.35f,
        CooperativeLiftEasingPower = 1.35f,
        RevealFeather = 40,
        GlowRadius = 5.5f,
        GlowOpacity = 0.28f,
        ScanGlowRadius = 3.5f,
        ScanGlowOpacity = 0.2f,
        ScanGlowEdgeWidth = 18,
        HighlightBackgroundAlpha = 0.08f,
        HighlightCornerRadius = 16,
        HighlightMargin = 18,
        TouchDownScale = 0.95f,
        DeselectedScale = 0.985f,
        SelectedScale = 1.065f,
        DeselectedLineOpacity = 0.24f,
        MinimumScaleOpacity = 0.9f,
        TextPadding = 20,
        TransliterationSpacing = 12,
        HiddenOnBlurFontScale = 0.55f,
        UnscannedTextOpacity = 0.24f,
        TokenMinOpacity = 0.28f,
        TokenMaxOpacity = 1,
        BlurRadiusPerLine = 1.6f,
        MaxBlurRadius = 9,
        FadeDistanceBase = 8,
        MaxFadeOpacityLoss = 0.72f,
        HoverHighlightExtraWidth = 6,
        HoverHighlightExtraHeight = 10,
        LineChangeDurationMs = 560,
        LineChangeEasingPower = 2.6f
    };

    public static LyricRenderSpecs Legacy { get; } = new()
    {
        MaxSelectedLines = 1,
        AnimationHeadstartMs = 0,
        LineDelayMs = 0,
        LineFinishProgressAnimationDurationMs = 0,
        TapProgressFreezeDurationMs = 0,
        ScaleAnimationDurationMs = 500,
        ScaleEnterAnimationDurationMs = 1000,
        SyllableLift = 3,
        ShortTokenLiftDurationThresholdMs = 1000,
        CooperativeLiftWindow = 0,
        CooperativeLiftEasingPower = 1,
        RevealFeather = 0,
        GlowRadius = 6,
        GlowOpacity = 0.4f,
        ScanGlowRadius = 0,
        ScanGlowOpacity = 0,
        ScanGlowEdgeWidth = 0,
        HighlightBackgroundAlpha = 10f / 255f,
        HighlightCornerRadius = 6,
        HighlightMargin = 0,
        TouchDownScale = 1,
        DeselectedScale = 0.8f,
        SelectedScale = 1,
        DeselectedLineOpacity = 0.3f,
        MinimumScaleOpacity = 0.5f,
        TextPadding = 16,
        TransliterationSpacing = 10,
        HiddenOnBlurFontScale = 0.5f,
        UnscannedTextOpacity = 0.3f,
        TokenMinOpacity = 0.3f,
        TokenMaxOpacity = 1,
        BlurRadiusPerLine = 1,
        MaxBlurRadius = 250,
        FadeDistanceBase = 10,
        MaxFadeOpacityLoss = 0.9f,
        HoverHighlightExtraWidth = 2,
        HoverHighlightExtraHeight = 8,
        LineChangeDurationMs = 1300,
        LineChangeEasingPower = 1f
    };

    public int MaxSelectedLines { get; init; }
    public long AnimationHeadstartMs { get; init; }
    public long LineDelayMs { get; init; }
    public long LineFinishProgressAnimationDurationMs { get; init; }
    public long TapProgressFreezeDurationMs { get; init; }
    public long ScaleAnimationDurationMs { get; init; }
    public long ScaleEnterAnimationDurationMs { get; init; }
    public float SyllableLift { get; init; }
    public long ShortTokenLiftDurationThresholdMs { get; init; }
    public float CooperativeLiftWindow { get; init; }
    public float CooperativeLiftEasingPower { get; init; }
    public float RevealFeather { get; init; }
    public float GlowRadius { get; init; }
    public float GlowOpacity { get; init; }
    public float ScanGlowRadius { get; init; }
    public float ScanGlowOpacity { get; init; }
    public float ScanGlowEdgeWidth { get; init; }
    public float HighlightBackgroundAlpha { get; init; }
    public float HighlightCornerRadius { get; init; }
    public float HighlightMargin { get; init; }
    public float TouchDownScale { get; init; }
    public float DeselectedScale { get; init; }
    public float SelectedScale { get; init; }
    public float DeselectedLineOpacity { get; init; }
    public float MinimumScaleOpacity { get; init; }
    public float TextPadding { get; init; }
    public float TransliterationSpacing { get; init; }
    public float HiddenOnBlurFontScale { get; init; }
    public float UnscannedTextOpacity { get; init; }
    public float TokenMinOpacity { get; init; }
    public float TokenMaxOpacity { get; init; }
    public float BlurRadiusPerLine { get; init; }
    public float MaxBlurRadius { get; init; }
    public float FadeDistanceBase { get; init; }
    public float MaxFadeOpacityLoss { get; init; }
    public float HoverHighlightExtraWidth { get; init; }
    public float HoverHighlightExtraHeight { get; init; }
    public long LineChangeDurationMs { get; init; }
    public float LineChangeEasingPower { get; init; }

    public static LyricRenderSpecs FromPreset(LyricRenderSpecPreset preset)
    {
        return preset switch
        {
            LyricRenderSpecPreset.Legacy => Legacy,
            _ => Modern
        };
    }
}
