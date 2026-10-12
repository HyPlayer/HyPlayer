using System;

namespace HyPlayer.LyricRenderer.Abstraction;

/// <summary>Built-in line appearances. These select fixed drawing formulas.</summary>
public enum LyricLinePreset { Default, Clear, SoftDepth, Layered, None }
/// <summary>Built-in lyric motion appearances.</summary>
public enum LyricHighlightPreset { Default, SoftLift }
public enum UntimedHighlightMode { DoNotHighlight, WholeLine, InferWords }
public enum HighlightRevealMode { RectangleClip, GlyphStep, WholeWord }
public enum TransliterationProgressMode { FollowMain, WholeLine }
public enum UntimedLiftMode { DoNotLift, WholeLine, InferWords }
public enum GlyphLiftUnit { Auto, Glyph, Word }
public enum GlyphLiftMotion { Hold, Pulse }

/// <summary>Immutable settings consumed once per frame; no executable configuration.</summary>
public sealed record LyricLineEffectSettings
{
    public LyricLinePreset Preset { get; init; } = LyricLinePreset.SoftDepth;
    public float GlowRadius { get; init; } = 6;
    public float GlowOpacity { get; init; } = 0.4f;
    public float NearOpacity { get; init; } = 0.5f;
    public float FarOpacity { get; init; } = 0.05f;
    public float MaximumBlur { get; init; } = 16;
    public float DistantScale { get; init; } = 0.72f;
    public bool HoverBackground { get; init; } = true;
    public bool Background { get; init; }
    public bool ProgressUnderline { get; init; }

    public static LyricLineEffectSettings ForPreset(LyricLinePreset preset) => preset switch
    {
        LyricLinePreset.Clear => new() { Preset = preset, NearOpacity = 0.72f, FarOpacity = 0.4f, MaximumBlur = 0, DistantScale = 0.9f },
        LyricLinePreset.Layered => new() { Preset = preset, NearOpacity = 0.4f, FarOpacity = 0, MaximumBlur = 6, DistantScale = 0.8f },
        LyricLinePreset.None => new() { Preset = preset, GlowRadius = 0, GlowOpacity = 0, NearOpacity = 1, FarOpacity = 1, MaximumBlur = 0, DistantScale = 1, HoverBackground = false },
        LyricLinePreset.Default => new() { Preset = preset },
        _ => new()
    };

    public LyricLineEffectSettings Normalize() => this with
    {
        Preset = Enum.IsDefined(Preset) ? Preset : LyricLinePreset.SoftDepth,
        GlowRadius = Limit(GlowRadius, 0, 64, 6), GlowOpacity = Limit(GlowOpacity, 0, 1, 0.4f),
        NearOpacity = Limit(NearOpacity, 0, 1, 0.5f), FarOpacity = Limit(FarOpacity, 0, 1, 0.05f),
        MaximumBlur = Limit(MaximumBlur, 0, 64, 16), DistantScale = Limit(DistantScale, 0.1f, 1, 0.72f)
    };

    internal static float Limit(float value, float minimum, float maximum, float fallback) =>
        float.IsFinite(value) ? Math.Clamp(value, minimum, maximum) : fallback;
}

/// <summary>Fixed highlight and lift parameters, independent of the line appearance.</summary>
public sealed record FocusedTextSettings
{
    public LyricHighlightPreset Preset { get; init; } = LyricHighlightPreset.SoftLift;
    public HighlightRevealMode RevealMode { get; init; } = HighlightRevealMode.RectangleClip;
    public UntimedHighlightMode UntimedHighlight { get; init; } = UntimedHighlightMode.InferWords;
    public TransliterationProgressMode Transliteration { get; init; } = TransliterationProgressMode.FollowMain;
    public float FeatherDip { get; init; } = 40;
    public float RevealTimeOffsetMs { get; init; }
    public bool LiftEnabled { get; init; } = true;
    public float LiftHeight { get; init; } = 2;
    public float LiftOverlap { get; init; } = 0.7f;
    public float WholeWordThresholdMs { get; init; } = 1000;
    public float LiftTimeOffsetMs { get; init; }
    public float LiftFinishDurationMs { get; init; } = 250;
    public GlyphLiftUnit LiftUnit { get; init; } = GlyphLiftUnit.Auto;
    public GlyphLiftMotion LiftMotion { get; init; } = GlyphLiftMotion.Hold;
    public UntimedLiftMode UntimedLift { get; init; } = UntimedLiftMode.InferWords;

    public bool InferWords => UntimedHighlight == UntimedHighlightMode.InferWords ||
                              LiftEnabled && UntimedLift == UntimedLiftMode.InferWords;

    public static FocusedTextSettings ForPreset(LyricHighlightPreset preset) => preset switch
    {
        LyricHighlightPreset.Default => new() { Preset = preset, LiftHeight = 3, UntimedLift = UntimedLiftMode.DoNotLift },
        _ => new()
    };

    public FocusedTextSettings Normalize() => this with
    {
        Preset = Enum.IsDefined(Preset) ? Preset : LyricHighlightPreset.SoftLift,
        RevealMode = Enum.IsDefined(RevealMode) ? RevealMode : HighlightRevealMode.RectangleClip,
        UntimedHighlight = Enum.IsDefined(UntimedHighlight) ? UntimedHighlight : UntimedHighlightMode.InferWords,
        Transliteration = Enum.IsDefined(Transliteration) ? Transliteration : TransliterationProgressMode.FollowMain,
        UntimedLift = Enum.IsDefined(UntimedLift) ? UntimedLift : UntimedLiftMode.InferWords,
        LiftUnit = Enum.IsDefined(LiftUnit) ? LiftUnit : GlyphLiftUnit.Auto,
        LiftMotion = Enum.IsDefined(LiftMotion) ? LiftMotion : GlyphLiftMotion.Hold,
        FeatherDip = LyricLineEffectSettings.Limit(FeatherDip, 0, 200, 40),
        RevealTimeOffsetMs = LyricLineEffectSettings.Limit(RevealTimeOffsetMs, -10000, 10000, 0),
        LiftHeight = LyricLineEffectSettings.Limit(LiftHeight, 0, 32, 2),
        LiftOverlap = LyricLineEffectSettings.Limit(LiftOverlap, 0, 1, 0.7f),
        WholeWordThresholdMs = LyricLineEffectSettings.Limit(WholeWordThresholdMs, 0, 10000, 1000),
        LiftTimeOffsetMs = LyricLineEffectSettings.Limit(LiftTimeOffsetMs, -10000, 10000, 0),
        LiftFinishDurationMs = LyricLineEffectSettings.Limit(LiftFinishDurationMs, 0, 10000, 250)
    };
}

/// <summary>Atomically transfers the two immutable settings snapshots to a canvas.</summary>
public sealed record LyricRenderSettings(LyricLineEffectSettings Line, FocusedTextSettings Focused)
{
    public static LyricRenderSettings Default { get; } = new(new(), new());
}
