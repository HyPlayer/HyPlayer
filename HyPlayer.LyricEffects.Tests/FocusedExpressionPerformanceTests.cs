using AwesomeAssertions;
using HyPlayer.LyricEffects.Expressions;

namespace HyPlayer.LyricEffects.Tests;

public class FocusedExpressionPerformanceTests
{
    private readonly LyricExpressionCompiler _compiler = new();

    [Test]
    public async Task Compiler_ShouldClassifyFocusedExpressionDependencies()
    {
        _compiler.CompileFocusedScalar("3").Dependencies.Should().Be(FocusedTextExpressionDependencies.None);
        _compiler.CompileFocusedScalar("line.Progress").Dependencies.Should().Be(FocusedTextExpressionDependencies.Line);
        _compiler.CompileFocusedScalar("frame.CurrentTimeMs / 1000f").Dependencies.Should().Be(FocusedTextExpressionDependencies.Frame);
        _compiler.CompileFocusedScalar("text.IsLyric ? 1 : 0").Dependencies.Should().Be(FocusedTextExpressionDependencies.Text);
        _compiler.CompileFocusedScalar("word.Progress").Dependencies.Should().Be(FocusedTextExpressionDependencies.Word);
        _compiler.CompileFocusedScalar("glyph.LiftProgress").Dependencies.Should().Be(FocusedTextExpressionDependencies.Glyph);
        _compiler.CompileFocusedScalar("fx.Sin(line.Progress)").Dependencies.Should().Be(FocusedTextExpressionDependencies.Line);
        _compiler.CompileFocusedColor("rgba(12, 34, 56, 0.5)").Dependencies.Should().Be(FocusedTextExpressionDependencies.None);
        await Task.CompletedTask;
    }

    [Test]
    public async Task FrameCache_ShouldEvaluateAtNarrowestRequiredScope()
    {
        var cache = new FocusedTextExpressionFrameCache();
        var sample = FocusedTextExpressionSamples.All[0];
        var evaluations = 0;
        FocusedTextScalarExpression expression = (line, _, _, _, _, _) =>
        {
            evaluations++;
            return line.Progress;
        };

        for (var glyphIndex = 0; glyphIndex < 100; glyphIndex++)
        {
            var glyph = sample.Glyph with { Index = glyphIndex };
            for (var contribution = 0; contribution < 2; contribution++)
                _ = cache.EvaluateScalar(1, FocusedTextExpressionDependencies.Line, expression,
                    sample.Line, sample.Frame, sample.Text, sample.Word, glyph);
        }
        evaluations.Should().Be(1);

        cache.Clear();
        evaluations = 0;
        for (var glyphIndex = 0; glyphIndex < 100; glyphIndex++)
        {
            var word = sample.Word with { Index = glyphIndex / 10 };
            var glyph = sample.Glyph with { Index = glyphIndex };
            for (var contribution = 0; contribution < 2; contribution++)
                _ = cache.EvaluateScalar(2, FocusedTextExpressionDependencies.Word, expression,
                    sample.Line, sample.Frame, sample.Text, word, glyph);
        }
        evaluations.Should().Be(10);

        cache.Clear();
        evaluations = 0;
        for (var glyphIndex = 0; glyphIndex < 100; glyphIndex++)
        {
            var glyph = sample.Glyph with { Index = glyphIndex };
            for (var contribution = 0; contribution < 2; contribution++)
                _ = cache.EvaluateScalar(3, FocusedTextExpressionDependencies.Glyph, expression,
                    sample.Line, sample.Frame, sample.Text, sample.Word, glyph);
        }
        evaluations.Should().Be(100);
        await Task.CompletedTask;
    }

    [Test]
    public void AotEvaluators_ShouldNotAllocatePerEvaluation()
    {
        var expression = _compiler.CompileScalar(
            "line.IsActive ? 1 : (frame.IsScrolling ? fx.Max(fx.Clamp(fx.Lerp(0.4, 0, line.ViewportDistance), 0, 1), 0.4) : fx.Clamp(fx.Lerp(0.4, 0, line.ViewportDistance), 0, 1))").Expression!;
        var sample = LyricExpressionSamples.All[0];
        for (var index = 0; index < 100; index++)
            _ = expression(sample.Line, sample.Frame, LyricExpressionFunctions.Instance);

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var index = 0; index < 1_000; index++)
            _ = expression(sample.Line, sample.Frame, LyricExpressionFunctions.Instance);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        allocated.Should().Be(0);

        var focused = _compiler.CompileFocusedScalar(
            "glyph.LiftProgress * fx.Sin(word.Progress) + line.Progress").Expression!;
        var focusedSample = FocusedTextExpressionSamples.All[0];
        for (var index = 0; index < 100; index++)
            _ = focused(focusedSample.Line, focusedSample.Frame, focusedSample.Text, focusedSample.Word,
                focusedSample.Glyph, LyricExpressionFunctions.Instance);

        before = GC.GetAllocatedBytesForCurrentThread();
        for (var index = 0; index < 1_000; index++)
            _ = focused(focusedSample.Line, focusedSample.Frame, focusedSample.Text, focusedSample.Word,
                focusedSample.Glyph, LyricExpressionFunctions.Instance);
        allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        allocated.Should().Be(0);
    }
}
