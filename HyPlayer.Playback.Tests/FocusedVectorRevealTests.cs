using HyPlayer.Features.Lyrics.Effects;
using HyPlayer.LyricRenderer.Text;
using HyPlayer.LyricEffects.Models;
using HyPlayer.LyricEffects.Presets;
using TUnit.Core;
using Windows.UI;

namespace HyPlayer.Playback.Tests;

public sealed class FocusedVectorRevealTests
{
    [Test]
    public void HighlightReveal_ShouldApplyToLyricsButNotTranslationWithoutTargets()
    {
        IReadOnlySet<string> noTargets = new HashSet<string>();

        if (FocusedLyricTextRenderer.ShouldApplyOperationToTarget(
                FocusedTextBuiltInOperationTypes.HighlightReveal,
                noTargets,
                FocusedTextTargets.Translation))
            throw new InvalidOperationException("高亮推进仍被应用到了翻译层。");
        if (!FocusedLyricTextRenderer.ShouldApplyOperationToTarget(
                FocusedTextBuiltInOperationTypes.HighlightReveal,
                noTargets,
                FocusedTextTargets.LyricCurrentHighlighted))
            throw new InvalidOperationException("无 Targets 的必选高亮推进没有应用到正文层。");
        if (FocusedLyricTextRenderer.ShouldApplyOperationToTarget(
                FocusedTextBuiltInOperationTypes.GlyphLift,
                noTargets,
                FocusedTextTargets.Translation))
            throw new InvalidOperationException("未选择 Translation target 时，逐字抬升仍被应用到了翻译层。");
    }

    [Test]
    public void UntimedContributions_ShouldBypassHighlightReveal()
    {
        foreach (var mode in Enum.GetValues<HighlightRevealMode>())
        {
            if (FocusedLyricTextRenderer.ShouldApplyHighlightReveal(
                    mode, participatesInReveal: false, isCurrentContribution: false) ||
                FocusedLyricTextRenderer.ShouldApplyHighlightReveal(
                    mode, participatesInReveal: false, isCurrentContribution: true))
                throw new InvalidOperationException(
                    $"{mode} 仍处理 WholeLine 或 DoNotHighlight 的静态贡献。");
        }
    }

    [Test]
    public void TimedContributions_ShouldPreserveRevealModeSemantics()
    {
        if (!FocusedLyricTextRenderer.ShouldApplyHighlightReveal(
                HighlightRevealMode.RectangleClip, participatesInReveal: true, isCurrentContribution: false))
            throw new InvalidOperationException("RectangleClip 不再裁剪已完成或尚未开始的定时贡献。");
        if (FocusedLyricTextRenderer.ShouldApplyHighlightReveal(
                HighlightRevealMode.GlyphStep, participatesInReveal: true, isCurrentContribution: false) ||
            FocusedLyricTextRenderer.ShouldApplyHighlightReveal(
                HighlightRevealMode.WholeWord, participatesInReveal: true, isCurrentContribution: false))
            throw new InvalidOperationException("非 RectangleClip 模式错误处理了非当前定时贡献。");
        if (!FocusedLyricTextRenderer.ShouldApplyHighlightReveal(
                HighlightRevealMode.GlyphStep, participatesInReveal: true, isCurrentContribution: true) ||
            !FocusedLyricTextRenderer.ShouldApplyHighlightReveal(
                HighlightRevealMode.WholeWord, participatesInReveal: true, isCurrentContribution: true))
            throw new InvalidOperationException("当前定时贡献没有按所选推进模式处理。");
    }

    [Test]
    public void ContributionBaseColor_ShouldUseAccentOnlyForHighlightedText()
    {
        var idle = Color.FromArgb(255, 10, 20, 30);
        var accent = Color.FromArgb(255, 200, 210, 220);
        string[] highlightedTargets =
        [
            FocusedTextTargets.LyricHighlighted,
            FocusedTextTargets.LyricCurrentHighlighted,
            FocusedTextTargets.TransliterationHighlighted,
            FocusedTextTargets.TransliterationCurrentHighlighted
        ];
        string[] idleTargets =
        [
            FocusedTextTargets.LyricCurrentPending,
            FocusedTextTargets.LyricUnhighlighted,
            FocusedTextTargets.TransliterationCurrentPending,
            FocusedTextTargets.TransliterationUnhighlighted,
            FocusedTextTargets.Translation
        ];

        foreach (var target in highlightedTargets)
            if (!FocusedLyricTextRenderer.GetContributionBaseColor(idle, accent, target).Equals(accent))
                throw new InvalidOperationException($"{target} 没有使用 AccentColor。");
        foreach (var target in idleTargets)
            if (!FocusedLyricTextRenderer.GetContributionBaseColor(idle, accent, target).Equals(idle))
                throw new InvalidOperationException($"{target} 没有使用 IdleColor。");
    }

    [Test]
    public void DefaultFocusedProfile_ShouldNotDimUnhighlightedText()
    {
        var profile = LyricEffectPresets.CreateDefaultFocusedText();

        if (profile.Operations.Any(operation =>
                operation.TypeId == FocusedTextBuiltInOperationTypes.Opacity))
            throw new InvalidOperationException("默认聚焦链仍包含降低未高亮歌词透明度的节点。");
    }

    [Test]
    public void LegacyDefaultOpacity_ShouldBeRemovedWithoutDeletingCustomOpacity()
    {
        var focusedText = new FocusedTextEffectDefinition
        {
            Operations =
            [
                new FocusedTextOperationDefinition
                {
                    TypeId = FocusedTextBuiltInOperationTypes.Opacity,
                    DisplayName = "未高亮透明度",
                    Targets =
                    [
                        FocusedTextTargets.LyricCurrentPending,
                        FocusedTextTargets.LyricUnhighlighted
                    ],
                    Parameters =
                    {
                        ["opacity"] = new LyricOperationParameterDefinition { Expression = "0.3" }
                    }
                },
                new FocusedTextOperationDefinition
                {
                    TypeId = FocusedTextBuiltInOperationTypes.Opacity,
                    DisplayName = "自定义透明度",
                    Targets = [FocusedTextTargets.LyricUnhighlighted],
                    Parameters =
                    {
                        ["opacity"] = new LyricOperationParameterDefinition { Expression = "0.4" }
                    }
                }
            ]
        };

        if (!LyricEffectProfileService.RemoveLegacyFocusedOpacity(focusedText) ||
            focusedText.Operations.Count != 1 ||
            focusedText.Operations[0].DisplayName != "自定义透明度")
            throw new InvalidOperationException("旧默认透明度节点未被精确迁移，或自定义透明度节点被误删。");
    }

    [Test]
    public void RectangleRevealPruning_ShouldNotReleaseTranslationClustersOneByOne()
    {
        IReadOnlySet<string> noTargets = new HashSet<string>();

        if (FocusedLyricTextRenderer.ShouldUseLineRevealForTarget(
                HighlightRevealMode.RectangleClip,
                noTargets,
                FocusedTextTargets.Translation))
            throw new InvalidOperationException(
                "未参与特效的 Translation 仍被行级 reveal 预裁剪，会导致字符随扫描边界逐个出现。");
    }

    [Test]
    public void FullyHighlightedCurrentGlyph_ShouldNotBeClippedToItsVisualBounds()
    {
        var clip = FocusedLyricTextRenderer.GetLineVectorContributionClip(
            10, 110, 110, highlighted: true, isRightToLeft: false, renderingHeight: 40);

        if (clip is not null)
            throw new InvalidOperationException(
                $"完整高亮贡献仍被裁成 Glyph 矩形：{clip.Value}，会切暗右侧抗锯齿边缘。");
    }

    [Test]
    public void FeatheredRectangleClip_ShouldNotRestartGradientAtEveryCompletedGlyph()
    {
        var highlighted = FocusedLyricTextRenderer.CreateRectangleMaskPlan(
            reveal: 1, highlighted: true, isRightToLeft: false, feather: 40, width: 24);
        var pending = FocusedLyricTextRenderer.CreateRectangleMaskPlan(
            reveal: 1, highlighted: false, isRightToLeft: false, feather: 40, width: 24);

        if (highlighted.ConstantOpacity != 1 || pending.ConstantOpacity != 0)
            throw new InvalidOperationException(
                "羽化 RectangleClip 在完整 Glyph 上重新生成渐变，会让当前 Word 的每个 Glyph 右侧重复变暗。");
    }

    [Test]
    public void FeatheredRectangleClip_ShouldContractTowardTheTerminalEdge()
    {
        var nearStart = FocusedLyricTextRenderer.CreateRectangleMaskPlan(
            reveal: 0.01f, highlighted: true, isRightToLeft: false, feather: 40, width: 80);
        var nearEnd = FocusedLyricTextRenderer.CreateRectangleMaskPlan(
            reveal: 0.99f, highlighted: true, isRightToLeft: false, feather: 40, width: 80);
        var last60FpsSample = FocusedLyricTextRenderer.CreateRectangleMaskPlan(
            reveal: 233f / 250f, highlighted: true, isRightToLeft: false, feather: 40, width: 80);

        if (nearEnd.FirstStop <= 0.95f || nearEnd.EndOpacity <= 0.95f ||
            last60FpsSample.EndOpacity <= 0.75f)
            throw new InvalidOperationException(
                $"羽化扫词在端点附近仍保留固定宽度暗尾：start={nearStart.FirstStop:P1}..{nearStart.SecondStop:P1}, " +
                $"end={nearEnd.FirstStop:P1}..{nearEnd.SecondStop:P1}, " +
                $"lastSampleAlpha={last60FpsSample.EndOpacity:P1}。");
    }

    [Test]
    public void FeatheredRectangleClip_ShouldEnterNewLineWithSolidLeadingEdge()
    {
        const float first60FpsSampleOf250MsWord = 1f / 15f;
        var timeline = FocusedLineRevealTimeline.Create(
            new[] { new FocusedLineRevealSpan(0, 250, 0, 80) }, 0, 80, false);
        var firstFrame = FocusedLyricTextRenderer.CreateRectangleMaskPlan(
            reveal: timeline.Sample((long)(250 * first60FpsSampleOf250MsWord)).Progress,
            highlighted: true,
            isRightToLeft: false,
            feather: 40,
            width: 80);

        if (firstFrame.StartOpacity < 0.999f)
            throw new InvalidOperationException(
                $"新视觉行首帧的最左端只有 {firstFrame.StartOpacity:P1}，羽化进入会导致首 Glyph 闪烁。");
    }

    [Test]
    public void FeatheredRectangleClip_ShouldKeepRtlAndPendingMasksComplementary()
    {
        foreach (var rtl in new[] { false, true })
        {
            var highlighted = FocusedLyricTextRenderer.CreateRectangleMaskPlan(
                reveal: 0.73f, highlighted: true, isRightToLeft: rtl, feather: 40, width: 80);
            var pending = FocusedLyricTextRenderer.CreateRectangleMaskPlan(
                reveal: 0.73f, highlighted: false, isRightToLeft: rtl, feather: 40, width: 80);

            if (Math.Abs(highlighted.StartOpacity + pending.StartOpacity - 1) > 0.0001f ||
                Math.Abs(highlighted.EndOpacity + pending.EndOpacity - 1) > 0.0001f ||
                Math.Abs(highlighted.MiddleOpacity + pending.MiddleOpacity - 1) > 0.0001f ||
                highlighted.FirstStop != pending.FirstStop ||
                highlighted.MiddleStop != pending.MiddleStop ||
                highlighted.SecondStop != pending.SecondStop)
                throw new InvalidOperationException($"RTL={rtl} 时高亮与待高亮遮罩不再互补。");
        }
    }

    [Test]
    public void LineFeather_ShouldFadeMoreLightlyWithoutReducingItsReach()
    {
        var highlighted = FocusedLyricTextRenderer.CreateRectangleMaskPlan(
            reveal: 0.5f, highlighted: true, isRightToLeft: false, feather: 40, width: 100);

        if (Math.Abs(highlighted.SecondStop - highlighted.FirstStop - 0.4f) > 0.001f ||
            highlighted.MiddleOpacity is < 0.05f or > 0.08f)
            throw new InvalidOperationException(
                $"羽化没有保持 40% 覆盖范围并减淡中段：width={highlighted.SecondStop - highlighted.FirstStop:P1}, " +
                $"middleAlpha={highlighted.MiddleOpacity:P1}。");
    }

    [Test]
    public void LineFeather_ShouldKeepItsWidthAcrossWordBoundary()
    {
        var timeline = FocusedLineRevealTimeline.Create(
            new[]
            {
                new FocusedLineRevealSpan(0, 250, 0, 50),
                new FocusedLineRevealSpan(250, 500, 50, 100)
            }, 0, 100, false);
        var before = FocusedLyricTextRenderer.CreateRectangleMaskPlan(
            timeline.Sample(249).Progress, true, false, 40, 100);
        var after = FocusedLyricTextRenderer.CreateRectangleMaskPlan(
            timeline.Sample(251).Progress, true, false, 40, 100);

        if (Math.Abs((before.SecondStop - before.FirstStop) - 0.4f) > 0.001f ||
            Math.Abs((after.SecondStop - after.FirstStop) - 0.4f) > 0.001f)
            throw new InvalidOperationException("羽化宽度在 Word 边界被重新收缩。");
    }

    [Test]
    public void LineFeather_AtWordEnd_ShouldHaveSolidEdgeAtWordBoundaryAndSpillForward()
    {
        var timeline = FocusedLineRevealTimeline.Create(
            new[]
            {
                new FocusedLineRevealSpan(0, 250, 0, 50),
                new FocusedLineRevealSpan(250, 500, 50, 100)
            }, 0, 100, false);
        var atFirstWordEnd = FocusedLyricTextRenderer.CreateRectangleMaskPlan(
            timeline.Sample(250).Progress, true, false, 40, 100);

        if (Math.Abs(atFirstWordEnd.FirstStop - 0.5f) > 0.001f ||
            Math.Abs(atFirstWordEnd.FirstOpacity - 1) > 0.001f ||
            atFirstWordEnd.SecondStop <= 0.5f)
            throw new InvalidOperationException(
                $"Word 结束锚点没有落在羽化内部实心端：solid={atFirstWordEnd.FirstStop:P1}, " +
                $"alpha={atFirstWordEnd.FirstOpacity:P1}, featherEnd={atFirstWordEnd.SecondStop:P1}。");
    }

    [Test]
    public void RtlLineFeather_AtWordEnd_ShouldHaveSolidEdgeAtWordBoundaryAndSpillForward()
    {
        var timeline = FocusedLineRevealTimeline.Create(
            new[]
            {
                new FocusedLineRevealSpan(0, 250, 100, 50),
                new FocusedLineRevealSpan(250, 500, 50, 0)
            }, 0, 100, true);
        var atFirstWordEnd = FocusedLyricTextRenderer.CreateRectangleMaskPlan(
            timeline.Sample(250).Progress, true, true, 40, 100);

        if (Math.Abs(atFirstWordEnd.SecondStop - 0.5f) > 0.001f ||
            Math.Abs(atFirstWordEnd.SecondOpacity - 1) > 0.001f ||
            atFirstWordEnd.FirstStop >= 0.5f)
            throw new InvalidOperationException(
                $"RTL Word 结束锚点没有落在羽化内部实心端：featherEnd={atFirstWordEnd.FirstStop:P1}, " +
                $"solid={atFirstWordEnd.SecondStop:P1}, alpha={atFirstWordEnd.SecondOpacity:P1}。");
    }

    [Test]
    public void LineVectorClip_ShouldRemainComplementaryAndCoverLiftOutsets()
    {
        var highlighted = FocusedLyricTextRenderer.GetLineVectorContributionClip(
            20, 80, 45, true, false, 40);
        var pending = FocusedLyricTextRenderer.GetLineVectorContributionClip(
            20, 80, 45, false, false, 40);

        if (highlighted is null || pending is null ||
            Math.Abs(highlighted.Value.Right - pending.Value.Left) > 0.001f ||
            highlighted.Value.Top >= 0 || highlighted.Value.Bottom <= 40)
            throw new InvalidOperationException("行级 Vector 遮罩不互补，或会裁掉抬升后的 Glyph。");
    }
}
