using AwesomeAssertions;
using HyPlayer.LyricEffects.Models;

namespace HyPlayer.LyricEffects.Tests;

public class FocusedTextProgressTests
{
    [Test]
    public async Task Overlap_ShouldControlGlyphWindowWithoutChangingRevealMode()
    {
        FocusedTextProgress.GetGlyphWindowProgress(0.25f, 1, 3, 0).Should().Be(0);
        FocusedTextProgress.GetGlyphWindowProgress(0.25f, 1, 3, 1).Should().Be(0.25f);
        FocusedTextProgress.GetRevealProgress(HighlightRevealMode.RectangleClip, 0.5f, 1, 3)
            .Should().Be(0.5f);
        await Task.CompletedTask;
    }

    [Test]
    public async Task ShortWord_ShouldLiftAsWholeWord_AndPulseShouldReturn()
    {
        var first = FocusedTextProgress.GetLiftProgress(0.25f, 800, 0, 3, 0, 1000, GlyphLiftUnit.Auto, GlyphLiftMotion.Hold);
        var last = FocusedTextProgress.GetLiftProgress(0.25f, 800, 2, 3, 0, 1000, GlyphLiftUnit.Auto, GlyphLiftMotion.Hold);
        first.Should().Be(last);

        FocusedTextProgress.GetLiftProgress(0.75f, 800, 0, 3, 0, 1000, GlyphLiftUnit.Auto, GlyphLiftMotion.Pulse)
            .Should().Be(0.5f);
        await Task.CompletedTask;
    }

    [Test]
    public async Task RevealAndMotion_ShouldRemainOrthogonalForAllNineCombinations()
    {
        var revealModes = new[]
        {
            HighlightRevealMode.RectangleClip,
            HighlightRevealMode.GlyphStep,
            HighlightRevealMode.WholeWord
        };
        GlyphLiftMotion?[] motions = [null, GlyphLiftMotion.Hold, GlyphLiftMotion.Pulse];

        foreach (var revealMode in revealModes)
        {
            var expectedReveal = FocusedTextProgress.GetRevealProgress(revealMode, 0.6f, 1, 4);
            foreach (var motion in motions)
            {
                if (motion is { } value)
                {
                    _ = FocusedTextProgress.GetLiftProgress(0.6f, 1500, 1, 4, 0.5f, 1000, GlyphLiftUnit.Auto, value);
                }

                FocusedTextProgress.GetRevealProgress(revealMode, 0.6f, 1, 4)
                    .Should().Be(expectedReveal);
            }
        }

        await Task.CompletedTask;
    }

    [Test]
    public async Task OverlapAndThresholdBoundaries_ShouldBeStable()
    {
        var noOverlap = FocusedTextProgress.GetLiftProgress(0.3f, 1001, 1, 3, 0, 1000, GlyphLiftUnit.Auto, GlyphLiftMotion.Hold);
        var halfOverlap = FocusedTextProgress.GetLiftProgress(0.3f, 1001, 1, 3, 0.5f, 1000, GlyphLiftUnit.Auto, GlyphLiftMotion.Hold);
        var fullOverlap = FocusedTextProgress.GetLiftProgress(0.3f, 1001, 1, 3, 1, 1000, GlyphLiftUnit.Auto, GlyphLiftMotion.Hold);
        noOverlap.Should().Be(0);
        halfOverlap.Should().BeGreaterThan(noOverlap);
        fullOverlap.Should().Be(0.3f);

        FocusedTextProgress.GetLiftProgress(0.2f, 1000, 2, 3, 0, 1000, GlyphLiftUnit.Auto, GlyphLiftMotion.Hold)
            .Should().Be(0.2f);
        FocusedTextProgress.GetLiftProgress(0.2f, 1001, 2, 3, 0, 1000, GlyphLiftUnit.Auto, GlyphLiftMotion.Hold)
            .Should().Be(0);
        await Task.CompletedTask;
    }

    [Test]
    public async Task Elastic_ShouldKeepEndpoints_AndUseLinearLimitAtZeroSpringiness()
    {
        FocusedTextProgress.GetElasticProgress(0, 6, 1.25).Should().Be(0);
        FocusedTextProgress.GetElasticProgress(1, 6, 1.25).Should().Be(1);
        FocusedTextProgress.GetElasticProgress(0.37, 0, 99).Should().Be(0.37);
        await Task.CompletedTask;
    }

    [Test]
    public async Task ReversedLiftWindow_ShouldStepAccordingToMotion()
    {
        FocusedTextProgress.GetTimedProgress(900, 1000, 1100, -200, 0, GlyphLiftMotion.Hold)
            .Should().Be(0);
        FocusedTextProgress.GetTimedProgress(1200, 1000, 1100, -200, 0, GlyphLiftMotion.Hold)
            .Should().Be(1);
        FocusedTextProgress.GetTimedProgress(1200, 1000, 1100, -200, 0, GlyphLiftMotion.Pulse)
            .Should().Be(0);
        await Task.CompletedTask;
    }
}
