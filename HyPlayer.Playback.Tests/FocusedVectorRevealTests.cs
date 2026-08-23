using HyPlayer.LyricRenderer.Text;
using TUnit.Core;

namespace HyPlayer.Playback.Tests;

public sealed class FocusedVectorRevealTests
{
    [Test]
    public void FullyHighlightedCurrentGlyph_ShouldNotBeClippedToItsVisualBounds()
    {
        var clip = FocusedLyricTextRenderer.GetVectorContributionClip(
            10, 20, 100, 40, 1, highlighted: true, isRightToLeft: false);

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

        if (nearStart.SecondStop >= 0.05f || nearStart.StartOpacity >= 0.05f ||
            nearEnd.FirstStop <= 0.95f || nearEnd.EndOpacity <= 0.95f ||
            last60FpsSample.EndOpacity <= 0.75f)
            throw new InvalidOperationException(
                $"羽化扫词在端点附近仍保留固定宽度暗尾：start={nearStart.FirstStop:P1}..{nearStart.SecondStop:P1}, " +
                $"end={nearEnd.FirstStop:P1}..{nearEnd.SecondStop:P1}, " +
                $"lastSampleAlpha={last60FpsSample.EndOpacity:P1}。");
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
                highlighted.FirstStop != pending.FirstStop || highlighted.SecondStop != pending.SecondStop)
                throw new InvalidOperationException($"RTL={rtl} 时高亮与待高亮遮罩不再互补。");
        }
    }
}
