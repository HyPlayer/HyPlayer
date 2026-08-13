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
}
