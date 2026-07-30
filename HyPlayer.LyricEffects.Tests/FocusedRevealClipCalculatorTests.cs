using AwesomeAssertions;
using HyPlayer.LyricEffects.Models;

namespace HyPlayer.LyricEffects.Tests;

public class FocusedRevealClipCalculatorTests
{
    private static readonly FocusedEffectOutsets Outsets = new(6, 7, 8, 9);

    [Test]
    public async Task FullContribution_ShouldNotCreateAGlyphSizedClip()
    {
        FocusedRevealClipCalculator.GetContributionClip(
                10, 20, 100, 40, 0, highlighted: false, isRightToLeft: false, Outsets)
            .Should().BeNull();
        FocusedRevealClipCalculator.GetContributionClip(
                10, 20, 100, 40, 1, highlighted: true, isRightToLeft: false, Outsets)
            .Should().BeNull();

        await Task.CompletedTask;
    }

    [Test]
    public async Task LtrPartialContribution_ShouldExpandAwayFromRevealBoundary()
    {
        var highlighted = FocusedRevealClipCalculator.GetContributionClip(
            10, 20, 100, 40, 0.4f, highlighted: true, isRightToLeft: false, Outsets);
        var pending = FocusedRevealClipCalculator.GetContributionClip(
            10, 20, 100, 40, 0.4f, highlighted: false, isRightToLeft: false, Outsets);

        highlighted.Should().Be(new FocusedRevealClip(4, 13, 46, 56));
        pending.Should().Be(new FocusedRevealClip(50, 13, 68, 56));
        highlighted!.Value.Right.Should().Be(pending!.Value.Left);

        await Task.CompletedTask;
    }

    [Test]
    public async Task RtlPartialContribution_ShouldReverseExpansionSides()
    {
        var highlighted = FocusedRevealClipCalculator.GetContributionClip(
            10, 20, 100, 40, 0.4f, highlighted: true, isRightToLeft: true, Outsets);
        var pending = FocusedRevealClipCalculator.GetContributionClip(
            10, 20, 100, 40, 0.4f, highlighted: false, isRightToLeft: true, Outsets);

        highlighted.Should().Be(new FocusedRevealClip(70, 13, 48, 56));
        pending.Should().Be(new FocusedRevealClip(4, 13, 66, 56));
        pending!.Value.Right.Should().Be(highlighted!.Value.Left);

        await Task.CompletedTask;
    }

    [Test]
    public async Task ShiftedGlyph_ShouldShiftClipWithoutChangingItsSize()
    {
        var original = FocusedRevealClipCalculator.GetContributionClip(
            10, 20, 100, 40, 0.4f, highlighted: true, isRightToLeft: false, Outsets);
        var lifted = FocusedRevealClipCalculator.GetContributionClip(
            13, 15, 100, 40, 0.4f, highlighted: true, isRightToLeft: false, Outsets);

        lifted!.Value.Left.Should().Be(original!.Value.Left + 3);
        lifted.Value.Top.Should().Be(original.Value.Top - 5);
        lifted.Value.Width.Should().Be(original.Value.Width);
        lifted.Value.Height.Should().Be(original.Value.Height);

        await Task.CompletedTask;
    }
}
