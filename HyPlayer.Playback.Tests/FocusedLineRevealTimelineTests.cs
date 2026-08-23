using HyPlayer.LyricRenderer.Text;
using TUnit.Core;

namespace HyPlayer.Playback.Tests;

public sealed class FocusedLineRevealTimelineTests
{
    [Test]
    public void Timeline_ShouldPassTimedEdgesWithoutReversing()
    {
        var timeline = FocusedLineRevealTimeline.Create(
        new[]
        {
            new FocusedLineRevealSpan(0, 300, 10, 40),
            new FocusedLineRevealSpan(450, 900, 50, 90)
        }, 10, 90, false);

        AssertNear(timeline.Sample(0).Position, 10);
        AssertNear(timeline.Sample(300).Position, 40);
        AssertNear(timeline.Sample(450).Position, 50);
        AssertNear(timeline.Sample(900).Position, 90);

        var previous = timeline.Sample(0).Progress;
        for (var time = 1; time <= 900; time++)
        {
            var current = timeline.Sample(time).Progress;
            if (current < previous)
                throw new InvalidOperationException($"行级扫词在 {time}ms 从 {previous:P2} 回退到 {current:P2}。");
            previous = current;
        }
    }

    [Test]
    public void Timeline_ShouldEaseLineEndpointsButNotRestartAtWordBoundary()
    {
        var timeline = FocusedLineRevealTimeline.Create(
        new[]
        {
            new FocusedLineRevealSpan(0, 250, 0, 50),
            new FocusedLineRevealSpan(250, 500, 50, 100)
        }, 0, 100, false);

        var firstFrame = timeline.Sample(1000 / 60).Progress;
        var beforeBoundary = timeline.Sample(249).Position;
        var atBoundary = timeline.Sample(250).Position;
        var afterBoundary = timeline.Sample(251).Position;

        if (firstFrame >= 0.02f)
            throw new InvalidOperationException($"视觉行首帧推进到 {firstFrame:P1}，起步仍然过快。");
        if (Math.Abs(atBoundary - 50) > 0.001f ||
            Math.Abs(atBoundary - beforeBoundary) > 0.5f ||
            Math.Abs(afterBoundary - atBoundary) > 0.5f)
            throw new InvalidOperationException("Word 边界处的行级扫词位置不连续。");
    }

    [Test]
    public void Timeline_ShouldReversePhysicalPositionForRtl()
    {
        var timeline = FocusedLineRevealTimeline.Create(
            new[] { new FocusedLineRevealSpan(0, 1000, 100, 0) }, 0, 100, true);

        AssertNear(timeline.Sample(0).Position, 100);
        AssertNear(timeline.Sample(1000).Position, 0);
        if (timeline.Sample(750).Position >= timeline.Sample(250).Position)
            throw new InvalidOperationException("RTL 行级扫词没有从右向左推进。");
    }

    [Test]
    public void Timeline_ShouldCoalesceOverlappingAndZeroDurationAnchors()
    {
        var timeline = FocusedLineRevealTimeline.Create(
        new[]
        {
            new FocusedLineRevealSpan(0, 400, 0, 40),
            new FocusedLineRevealSpan(300, 300, 40, 60),
            new FocusedLineRevealSpan(350, 800, 60, 100)
        }, 0, 100, false);

        var previous = 0f;
        for (var time = 0; time <= 800; time++)
        {
            var current = timeline.Sample(time).Progress;
            if (!float.IsFinite(current) || current < previous)
                throw new InvalidOperationException("重叠或零时长锚点产生了无效/倒退进度。");
            previous = current;
        }
    }

    [Test]
    public void Timeline_ShouldExposeMixedDirectionSegmentsForAccumulatedMasks()
    {
        var first = new FocusedLineRevealSpan(0, 400, 0, 40);
        var second = new FocusedLineRevealSpan(400, 800, 100, 60);
        var timeline = FocusedLineRevealTimeline.Create(new[] { first, second }, 0, 100, false);

        if (timeline.IsSpatiallyMonotone)
            throw new InvalidOperationException("混合 BiDi 行被误判为单一方向，会擦除已经高亮的区域。");
        AssertNear(timeline.SampleSpan(first, 600).Progress, 1);
        AssertNear(timeline.SampleSpan(second, 400).Progress, 0);
    }

    [Test]
    public void WrappedVisualLines_ShouldAdvanceSequentially()
    {
        var firstLine = FocusedLineRevealTimeline.Create(
            new[] { new FocusedLineRevealSpan(0, 500, 0, 100) }, 0, 100, false);
        var secondLine = FocusedLineRevealTimeline.Create(
            new[] { new FocusedLineRevealSpan(500, 1000, 0, 100) }, 0, 100, false);

        if (firstLine.Sample(499).Progress < 0.99f || secondLine.Sample(499).Progress != 0 ||
            firstLine.Sample(501).Progress != 1 || secondLine.Sample(501).Progress > 0.01f)
            throw new InvalidOperationException("自动换行没有按视觉行依次推进。");
    }

    private static void AssertNear(float actual, float expected)
    {
        if (Math.Abs(actual - expected) > 0.001f)
            throw new InvalidOperationException($"期望 {expected}，实际 {actual}。");
    }
}
