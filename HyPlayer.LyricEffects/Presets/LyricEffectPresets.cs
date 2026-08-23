using HyPlayer.LyricEffects.Models;

namespace HyPlayer.LyricEffects.Presets;

public static class LyricBuiltInOperationTypes
{
    public const string Source = "hyplayer.draw.source";
    public const string Debug = "hyplayer.draw.debug";
    public const string Glow = "hyplayer.effect.glow";
    public const string Opacity = "hyplayer.effect.opacity";
    public const string GaussianBlur = "hyplayer.effect.gaussian-blur";
    public const string Transform2D = "hyplayer.effect.transform-2d";
    public const string Transform3D = "hyplayer.effect.transform-3d";
    public const string Background = "hyplayer.draw.background";
    public const string DrawScript = "hyplayer.draw.script";
}

public static class FocusedTextBuiltInOperationTypes
{
    public const string HighlightReveal = "hyplayer.focus.highlight-reveal";
    public const string Color = "hyplayer.focus.color";
    public const string Opacity = "hyplayer.focus.opacity";
    public const string Transform2D = "hyplayer.focus.transform-2d";
    public const string Transform3D = "hyplayer.focus.transform-3d";
    public const string GaussianBlur = "hyplayer.focus.gaussian-blur";
    public const string Glow = "hyplayer.focus.glow";
    public const string Stroke = "hyplayer.focus.stroke";
    public const string GlyphLift = "hyplayer.focus.glyph-lift";
    public const string DrawScript = "hyplayer.focus.draw-script";
}

public static class FocusedTextTargets
{
    public const string LyricHighlighted = "lyric.highlighted";
    public const string LyricCurrentHighlighted = "lyric.current-highlighted";
    public const string LyricCurrentPending = "lyric.current-pending";
    public const string LyricUnhighlighted = "lyric.unhighlighted";
    public const string TransliterationHighlighted = "transliteration.highlighted";
    public const string TransliterationCurrentHighlighted = "transliteration.current-highlighted";
    public const string TransliterationCurrentPending = "transliteration.current-pending";
    public const string TransliterationUnhighlighted = "transliteration.unhighlighted";
    public const string Translation = "translation";

    public static IReadOnlySet<string> All { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        LyricHighlighted,
        LyricCurrentHighlighted,
        LyricCurrentPending,
        LyricUnhighlighted,
        TransliterationHighlighted,
        TransliterationCurrentHighlighted,
        TransliterationCurrentPending,
        TransliterationUnhighlighted,
        Translation
    };
}

public sealed record LyricOperationPreset(string Name, string Description, LyricRenderOperationDefinition Operation);

public sealed record LyricProfilePreset(string Name, string Description, LyricEffectProfileDocument Profile);

public sealed record FocusedTextProfilePreset(string Name, string Description, FocusedTextEffectDefinition Profile);

public static class LyricEffectPresets
{
    public static IReadOnlyList<LyricOperationPreset> OperationPresets { get; } =
    [
        new("焦点辉光", "为当前文本行增加整体辉光。", CreateGlow()),
        new("距离渐隐", "根据行到当前行的视口距离改变透明度。", CreateOpacity()),
        new("距离模糊", "滚动停止后模糊远离当前行的歌词。", CreateBlur()),
        new("焦点缩放", "当前行保持原尺寸，远处歌词逐渐缩小。", CreateScale()),
        new("3D 扇形", "非当前行沿 Y 轴形成扇形透视。", CreateTransform3D(true)),
        new("行背景", "在当前链位置把圆角背景绘制到现有内容后方。", CreateBackground()),
        new("圆角悬停背景", "绘制歌词行的指针悬停背景。", CreateHoverBackground()),
        new("进度下划线", "在当前歌词下方绘制随进度增长的线条。", CreateProgressUnderline())
    ];

    public static IReadOnlyList<LyricProfilePreset> ProfilePresets { get; } =
    [
        new("HyPlayer 默认", "保留当前默认的辉光、渐隐、模糊与缩放。", CreateDefaultProfile()),
        new("清晰", "减少模糊，只保留轻微渐隐和缩放。", CreateClearProfile()),
        new("柔和景深", "增强模糊与距离渐隐。", CreateDepthProfile()),
        new("立体层叠", "启用 3D 扇形和轻微景深。", CreateThreeDimensionalProfile()),
        new("无特效", "仅保留基础歌词与调试绘制节点。", CreateNoEffectsProfile())
    ];

    public static IReadOnlyList<FocusedTextProfilePreset> FocusedTextProfilePresets { get; } =
    [
        new("HyPlayer 默认", "视觉行级连续高亮，真实 Word 自动选择整词或逐 GlyphUnit 抬升。", CreateDefaultFocusedText()),
        new("柔和抬升", "自动拆词、行级柔和高亮边缘和 Linear 抬升。", CreateSoftLiftFocusedText())
    ];

    public static LyricEffectProfileDocument CreateDefaultProfile() => new()
    {
        Name = "HyPlayer 默认",
        FocusedText = CreateDefaultFocusedText(),
        Operations =
        [
            CreateSource(),
            CreateGlow(),
            CreateOpacity(expression: "line.IsActive ? 1 : fx.Clamp(fx.Lerp(0.5, 0.05, line.ViewportDistance), 0.05, 1)"),
            CreateBlur(maximum: 16),
            CreateHoverBackground(),
            CreateScale(target: 0.72f),
            CreateDebug()
        ]
    };

    public static FocusedTextEffectDefinition CreateDefaultFocusedText() => new()
    {
        Operations =
        [
            CreateFocusedOpacity(),
            CreateHighlightReveal(),
            CreateFocusedGlyphLift()
        ]
    };

    public static FocusedTextEffectDefinition CreateSoftLiftFocusedText()
    {
        var definition = CreateDefaultFocusedText();
        var reveal = definition.Operations.Single(item => item.TypeId == FocusedTextBuiltInOperationTypes.HighlightReveal);
        reveal.Options["untimedMode"] = nameof(UntimedHighlightMode.InferWords);
        reveal.Parameters["featherDip"].Expression = "40";
        var lift = definition.Operations.Single(item => item.TypeId == FocusedTextBuiltInOperationTypes.GlyphLift);
        lift.Options["untimedMode"] = nameof(UntimedLiftMode.InferWords);
        lift.Options["easingId"] = "linear";
        lift.Options["easingMode"] = "in";
        lift.Parameters["height"].Expression = "2";
        lift.Parameters["liftFinishDurationMs"].Expression = "250";
        return definition;
    }

    public static FocusedTextOperationDefinition CreateHighlightReveal() => new()
    {
        TypeId = FocusedTextBuiltInOperationTypes.HighlightReveal,
        DisplayName = "高亮推进",
        IsEnabled = true,
        Parameters =
        {
            ["revealTimeOffsetMs"] = FocusedScalar("0"),
            ["featherDip"] = FocusedScalar("40")
        },
        Options =
        {
            ["untimedMode"] = nameof(UntimedHighlightMode.InferWords),
            ["revealMode"] = nameof(HighlightRevealMode.RectangleClip),
            ["transliterationMode"] = nameof(TransliterationProgressMode.FollowMain)
        }
    };

    public static FocusedTextOperationDefinition CreateFocusedOpacity() => new()
    {
        TypeId = FocusedTextBuiltInOperationTypes.Opacity,
        DisplayName = "未高亮透明度",
        Targets =
        [
            FocusedTextTargets.LyricCurrentPending,
            FocusedTextTargets.LyricUnhighlighted
        ],
        Parameters = { ["opacity"] = FocusedScalar("0.3") }
    };

    public static FocusedTextOperationDefinition CreateFocusedGlyphLift() => new()
    {
        TypeId = FocusedTextBuiltInOperationTypes.GlyphLift,
        DisplayName = "逐字抬升",
        Targets =
        [
            FocusedTextTargets.LyricHighlighted,
            FocusedTextTargets.LyricCurrentHighlighted,
            FocusedTextTargets.LyricCurrentPending
        ],
        Parameters =
        {
            ["height"] = FocusedScalar("3"),
            ["overlap"] = FocusedScalar("0.7"),
            ["wholeWordThresholdMs"] = FocusedScalar("1000"),
            ["liftTimeOffsetMs"] = FocusedScalar("0"),
            ["liftFinishDurationMs"] = FocusedScalar("250"),
            ["exponent"] = FocusedScalar("2"),
            ["springiness"] = FocusedScalar("6"),
            ["oscillations"] = FocusedScalar("1"),
            ["bounces"] = FocusedScalar("2"),
            ["bounciness"] = FocusedScalar("2")
        },
        Options =
        {
            ["untimedMode"] = nameof(UntimedLiftMode.DoNotLift),
            ["liftUnit"] = nameof(GlyphLiftUnit.Auto),
            ["motion"] = nameof(GlyphLiftMotion.Hold),
            ["easingId"] = "linear",
            ["easingMode"] = "out"
        }
    };

    public static LyricRenderOperationDefinition CloneOperation(LyricRenderOperationDefinition source)
    {
        return new LyricRenderOperationDefinition
        {
            InstanceId = Guid.NewGuid().ToString("N"),
            TypeId = source.TypeId,
            DisplayName = source.DisplayName,
            IsEnabled = source.IsEnabled,
            Parameters = source.Parameters.ToDictionary(
                pair => pair.Key,
                pair => new LyricOperationParameterDefinition
                {
                    Expression = pair.Value.Expression,
                    Transition = pair.Value.Transition is null
                        ? null
                        : new LyricTransitionDefinition
                        {
                            DurationMs = pair.Value.Transition.DurationMs,
                            EasingId = pair.Value.Transition.EasingId,
                            Mode = pair.Value.Transition.Mode,
                            Arguments = new Dictionary<string, string>(pair.Value.Transition.Arguments)
                        }
                }),
            Options = new Dictionary<string, string>(source.Options),
            Script = source.Script,
            ExtensionData = source.ExtensionData?.ToDictionary(pair => pair.Key, pair => pair.Value.Clone())
        };
    }

    public static LyricEffectProfileDocument CloneProfile(LyricEffectProfileDocument source, bool renewInstanceIds = false)
    {
        var clone = new LyricEffectProfileDocument
        {
            Format = source.Format,
            SchemaVersion = source.SchemaVersion,
            ExpressionApiVersion = source.ExpressionApiVersion,
            Name = source.Name,
            Operations = source.Operations.Select(CloneOperation).ToList(),
            FocusedText = CloneFocusedText(source.FocusedText, renewInstanceIds),
            ExtensionData = source.ExtensionData?.ToDictionary(pair => pair.Key, pair => pair.Value.Clone())
        };
        if (!renewInstanceIds)
        {
            for (var index = 0; index < clone.Operations.Count; index++)
                clone.Operations[index].InstanceId = source.Operations[index].InstanceId;
        }

        return clone;
    }

    public static FocusedTextEffectDefinition CloneFocusedText(
        FocusedTextEffectDefinition source,
        bool renewInstanceIds = false) => new()
    {
        Operations = source.Operations.Select(operation => CloneFocusedOperation(operation, renewInstanceIds)).ToList(),
        ExtensionData = source.ExtensionData?.ToDictionary(pair => pair.Key, pair => pair.Value.Clone())
    };

    public static FocusedTextOperationDefinition CloneFocusedOperation(
        FocusedTextOperationDefinition source,
        bool renewInstanceId = true) => new()
    {
        InstanceId = renewInstanceId ? Guid.NewGuid().ToString("N") : source.InstanceId,
        TypeId = source.TypeId,
        DisplayName = source.DisplayName,
        IsEnabled = source.IsEnabled,
        Targets = [.. source.Targets],
        Parameters = source.Parameters.ToDictionary(
            pair => pair.Key,
            pair => new LyricOperationParameterDefinition
            {
                Expression = pair.Value.Expression,
                Transition = pair.Value.Transition is null
                    ? null
                    : new LyricTransitionDefinition
                    {
                        DurationMs = pair.Value.Transition.DurationMs,
                        EasingId = pair.Value.Transition.EasingId,
                        Mode = pair.Value.Transition.Mode,
                        Arguments = new Dictionary<string, string>(pair.Value.Transition.Arguments)
                    }
            }),
        Options = new Dictionary<string, string>(source.Options),
        Script = source.Script,
        ExtensionData = source.ExtensionData?.ToDictionary(pair => pair.Key, pair => pair.Value.Clone())
    };

    public static LyricRenderOperationDefinition CreateSource() => new()
    {
        TypeId = LyricBuiltInOperationTypes.Source,
        DisplayName = "歌词内容",
        IsEnabled = true
    };

    public static LyricRenderOperationDefinition CreateDebug() => new()
    {
        TypeId = LyricBuiltInOperationTypes.Debug,
        DisplayName = "Debug 信息",
        IsEnabled = true
    };

    public static LyricRenderOperationDefinition CreateGlow(bool enabled = true) => new()
    {
        TypeId = LyricBuiltInOperationTypes.Glow,
        DisplayName = "焦点辉光",
        IsEnabled = enabled,
        Parameters =
        {
            ["x"] = Scalar("0"),
            ["y"] = Scalar("0"),
            ["blur"] = Scalar("line.IsText && line.IsActive ? 6 : 0"),
            ["opacity"] = Scalar("line.IsText && line.IsActive ? 0.4 : 0"),
            ["color"] = Color("line.FocusingColor")
        }
    };

    public static LyricRenderOperationDefinition CreateBackground(bool enabled = true) => new()
    {
        TypeId = LyricBuiltInOperationTypes.Background,
        DisplayName = "行背景",
        IsEnabled = enabled,
        Parameters =
        {
            ["color"] = Color("fx.Rgba(255, 255, 255, 0.04)"),
            ["opacity"] = Scalar("1"),
            ["marginX"] = Scalar("0"),
            ["marginY"] = Scalar("0"),
            ["cornerRadius"] = Scalar("6")
        }
    };

    public static LyricRenderOperationDefinition CreateOpacity(bool enabled = true, string? expression = null) => new()
    {
        TypeId = LyricBuiltInOperationTypes.Opacity,
        DisplayName = "距离渐隐",
        IsEnabled = enabled,
        Parameters =
        {
            ["opacity"] = Scalar(
                expression ?? "line.IsActive ? 1 : (frame.IsScrolling ? fx.Max(fx.Clamp(fx.Lerp(0.4, 0, line.ViewportDistance), 0, 1), 0.4) : fx.Clamp(fx.Lerp(0.4, 0, line.ViewportDistance), 0, 1))",
                "line.IsActive ? 0 : 500")
        }
    };

    public static LyricRenderOperationDefinition CreateBlur(bool enabled = true, float maximum = 10) => new()
    {
        TypeId = LyricBuiltInOperationTypes.GaussianBlur,
        DisplayName = "距离模糊",
        IsEnabled = enabled,
        Parameters =
        {
            ["amount"] = Scalar($"frame.IsScrolling ? 0 : fx.Lerp(0, {Format(maximum)}, line.ViewportDistance)")
        }
    };

    public static LyricRenderOperationDefinition CreateScale(bool enabled = true, float target = 0.5f) => new()
    {
        TypeId = LyricBuiltInOperationTypes.Transform2D,
        DisplayName = "焦点缩放",
        IsEnabled = enabled,
        Parameters =
        {
            ["x"] = Scalar("0"),
            ["y"] = Scalar("0"),
            ["scaleX"] = Scalar($"frame.IsScrolling ? fx.Max(fx.Lerp(1, {Format(target)}, line.ViewportDistance), 0.8) : fx.Lerp(1, {Format(target)}, line.ViewportDistance)"),
            ["scaleY"] = Scalar($"frame.IsScrolling ? fx.Max(fx.Lerp(1, {Format(target)}, line.ViewportDistance), 0.8) : fx.Lerp(1, {Format(target)}, line.ViewportDistance)"),
            ["rotation"] = Scalar("0"),
            ["anchorX"] = Scalar("line.AnchorX"),
            ["anchorY"] = Scalar("line.AnchorY")
        }
    };

    public static LyricRenderOperationDefinition CreateTransform3D(bool enabled) => new()
    {
        TypeId = LyricBuiltInOperationTypes.Transform3D,
        DisplayName = "3D 扇形",
        IsEnabled = enabled,
        Parameters =
        {
            ["angleX"] = Scalar("0"),
            ["angleY"] = Scalar("frame.IsScrolling || line.IsActive ? 0 : fx.Clamp(-15 * line.IndexDistance, -60, 60)", 1000),
            ["angleZ"] = Scalar("0"),
            ["depth"] = Scalar("3000"),
            ["anchorX"] = Scalar("line.AnchorX"),
            ["anchorY"] = Scalar("line.AnchorY")
        }
    };

    public static LyricRenderOperationDefinition CreateHoverBackground() => new()
    {
        TypeId = LyricBuiltInOperationTypes.DrawScript,
        DisplayName = "圆角悬停背景",
        IsEnabled = true,
        Options = { ["placement"] = "AboveSource" },
        Script = "FillRoundedRectangle(0, 0, line.Width + 2, line.Height + 8, 6, fx.Rgba(255, 255, 255, line.IsHovered && !line.IsHidden ? 0.04 : 0));"
    };

    public static LyricRenderOperationDefinition CreateProgressUnderline() => new()
    {
        TypeId = LyricBuiltInOperationTypes.DrawScript,
        DisplayName = "进度下划线",
        IsEnabled = true,
        Options = { ["placement"] = "AboveSource" },
        Script = "DrawLine(0, line.Height + 4, line.Width * line.Progress, line.Height + 4, fx.Rgba(255, 255, 255, line.IsActive ? 0.8 : 0), 2);"
    };

    private static LyricEffectProfileDocument CreateClearProfile() => new()
    {
        Name = "清晰",
        FocusedText = CreateDefaultFocusedText(),
        Operations = WithRequiredDrawingNodes(
            CreateGlow(),
            CreateOpacity(expression: "line.IsActive ? 1 : fx.Clamp(fx.Lerp(0.72, 0.4, line.ViewportDistance), 0.4, 1)"),
            CreateHoverBackground(),
            CreateScale(target: 0.9f))
    };

    private static LyricEffectProfileDocument CreateDepthProfile() => new()
    {
        Name = "柔和景深",
        FocusedText = CreateDefaultFocusedText(),
        Operations = WithRequiredDrawingNodes(
            CreateGlow(),
            CreateOpacity(expression: "line.IsActive ? 1 : fx.Clamp(fx.Lerp(0.5, 0.05, line.ViewportDistance), 0.05, 1)"),
            CreateBlur(maximum: 16),
            CreateHoverBackground(),
            CreateScale(target: 0.72f))
    };

    private static LyricEffectProfileDocument CreateThreeDimensionalProfile() => new()
    {
        Name = "立体层叠",
        FocusedText = CreateDefaultFocusedText(),
        Operations = WithRequiredDrawingNodes(
            CreateGlow(),
            CreateOpacity(),
            CreateBlur(maximum: 6),
            CreateHoverBackground(),
            CreateScale(target: 0.8f),
            CreateTransform3D(true))
    };

    private static LyricEffectProfileDocument CreateNoEffectsProfile() => new()
    {
        Name = "无特效",
        FocusedText = CreateDefaultFocusedText(),
        Operations = WithRequiredDrawingNodes()
    };

    private static List<LyricRenderOperationDefinition> WithRequiredDrawingNodes(
        params LyricRenderOperationDefinition[] operations)
    {
        var result = new List<LyricRenderOperationDefinition>(operations.Length + 2) { CreateSource() };
        result.AddRange(operations);
        result.Add(CreateDebug());
        return result;
    }

    private static LyricOperationParameterDefinition Scalar(string expression, double durationMs = 500) => new()
    {
        Expression = expression,
        Transition = new LyricTransitionDefinition
        {
            DurationMs = durationMs.ToString(System.Globalization.CultureInfo.InvariantCulture)
        }
    };

    private static LyricOperationParameterDefinition Scalar(string expression, string durationExpression) => new()
    {
        Expression = expression,
        Transition = new LyricTransitionDefinition { DurationMs = durationExpression }
    };

    private static LyricOperationParameterDefinition FocusedScalar(string expression) => new()
    {
        Expression = expression
    };

    private static LyricOperationParameterDefinition Color(string expression) => new() { Expression = expression };

    private static string Format(float value) => value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
