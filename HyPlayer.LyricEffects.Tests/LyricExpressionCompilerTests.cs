using AwesomeAssertions;
using HyPlayer.LyricEffects.Expressions;
using HyPlayer.LyricEffects.Presets;

namespace HyPlayer.LyricEffects.Tests;

public class LyricExpressionCompilerTests
{
    private readonly LyricExpressionCompiler _compiler = new();

    [Test]
    public async Task DefaultPresetExpressions_ShouldCompileAndEvaluate()
    {
        foreach (var operation in LyricEffectPresets.CreateDefaultProfile().Operations)
        {
            foreach (var (key, parameter) in operation.Parameters)
            {
                var result = key == "color"
                    ? _compiler.CompileColor(parameter.Expression).IsSuccess
                    : _compiler.CompileScalar(parameter.Expression).IsSuccess;
                result.Should().BeTrue($"{operation.TypeId}.{key} should compile");
            }
        }

        var expression = _compiler.CompileScalar("line.IsActive ? 1 : fx.Clamp(1 - line.ViewportDistance, 0, 1)");
        expression.IsSuccess.Should().BeTrue();
        expression.Expression!(Line(isActive: false, viewportDistance: 0.25f), Frame(), LyricExpressionFunctions.Instance)
            .Should().BeApproximately(0.75f, 0.0001f);
        await Task.CompletedTask;
    }

    [Test]
    [Arguments("new object()")]
    [Arguments("typeof(string).Assembly.FullName")]
    [Arguments("line.GetType().FullName")]
    [Arguments("x => x")]
    [Arguments("line.Index = 1")]
    [Arguments("System.Math.Abs(1)")]
    [Arguments("unknown + 1")]
    public async Task UnsafeOrUnknownSyntax_ShouldBeRejected(string source)
    {
        _compiler.CompileScalar(source).IsSuccess.Should().BeFalse();
        await Task.CompletedTask;
    }

    [Test]
    public async Task OversizedExpression_ShouldReportLocation()
    {
        var result = _compiler.CompileScalar(new string('1', LyricExpressionCompiler.MaximumExpressionLength + 1));
        result.IsSuccess.Should().BeFalse();
        result.Diagnostic.Should().NotBeNull();
        result.Diagnostic!.Line.Should().BeGreaterThan(0);
        result.Diagnostic.Column.Should().BeGreaterThan(0);
        await Task.CompletedTask;
    }

    [Test]
    public async Task ColorAndTextExpressions_ShouldBeStronglyTyped()
    {
        var color = _compiler.CompileColor("fx.LerpColor(line.IdleColor, line.AccentColor, line.Progress)");
        var text = _compiler.CompileText("line.Text");
        color.IsSuccess.Should().BeTrue();
        text.IsSuccess.Should().BeTrue();
        color.Expression!(Line(progress: 0.5f), Frame(), LyricExpressionFunctions.Instance).A.Should().Be(255);
        text.Expression!(Line(text: "hello"), Frame(), LyricExpressionFunctions.Instance).Should().Be("hello");
        await Task.CompletedTask;
    }

    [Test]
    public async Task Rgba_ShouldCreateCustomColorsInFocusedColorExpressions()
    {
        var result = _compiler.CompileFocusedColor("rgba(12, 34, 56, 0.5)");
        var sample = FocusedTextExpressionSamples.All[0];

        result.IsSuccess.Should().BeTrue();
        result.Expression!(sample.Line, sample.Frame, sample.Text, sample.Word, sample.Glyph,
                LyricExpressionFunctions.Instance)
            .Should().Be(new LyricColorValue(128, 12, 34, 56));

        await Task.CompletedTask;
    }

    [Test]
    public async Task FocusedExpressions_ShouldKeepRevealAndMotionIndependent()
    {
        var reveal = _compiler.CompileFocusedScalar("glyph.RevealProgress");
        var motion = _compiler.CompileFocusedScalar("glyph.MotionProgress");
        var sample = FocusedTextExpressionSamples.All[0];

        reveal.IsSuccess.Should().BeTrue();
        motion.IsSuccess.Should().BeTrue();
        reveal.Expression!(sample.Line, sample.Frame, sample.Text, sample.Word, sample.Glyph,
            LyricExpressionFunctions.Instance).Should().Be(0.5f);
        motion.Expression!(sample.Line, sample.Frame, sample.Text, sample.Word, sample.Glyph,
            LyricExpressionFunctions.Instance).Should().Be(0.4f);
        await Task.CompletedTask;
    }

    private static LyricExpressionLine Line(
        bool isActive = true,
        float viewportDistance = 0,
        float progress = 0,
        string text = "line") =>
        new(1, 0, 0, viewportDistance, isActive, false, false, false, true,
            0, 1000, progress, 300, 50, 150, 25, text,
            new LyricColorValue(255, 255, 255, 255),
            new LyricColorValue(255, 255, 210, 80));

    private static LyricExpressionFrame Frame() =>
        new(1, 500, 500, true, false, false, 0, 800, 600, 96, 120);
}
