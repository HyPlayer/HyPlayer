using AwesomeAssertions;
using HyPlayer.LyricEffects.Drawing;

namespace HyPlayer.LyricEffects.Tests;

public class LyricDrawScriptParserTests
{
    private readonly LyricDrawScriptParser _parser = new();

    [Test]
    public async Task Parser_ShouldKeepNestedArgumentsStringsAndComments()
    {
        var result = _parser.Parse(
            """
            // background
            Save();
            FillRoundedRectangle(0, 0, line.Width, line.Height, fx.Max(4, 6), fx.Rgba(255, 255, 255, 0.1));
            DrawText("a,b;c", 4, 4, 16, line.IdleColor)
            Restore();
            """);

        result.IsSuccess.Should().BeTrue();
        result.Commands.Should().HaveCount(4);
        result.Commands[1].Arguments.Should().HaveCount(6);
        result.Commands[2].Arguments[0].Should().Be("\"a,b;c\"");
        await Task.CompletedTask;
    }

    [Test]
    public async Task UnbalancedNestedExpression_ShouldHaveLineAndColumn()
    {
        var result = _parser.Parse("FillRectangle(0, fx.Max(1, 2), 3, 4, line.IdleColor;");
        result.IsSuccess.Should().BeFalse();
        result.Diagnostic!.Line.Should().Be(1);
        result.Diagnostic.Column.Should().BeGreaterThan(0);
        await Task.CompletedTask;
    }

    [Test]
    public async Task CommandCountAndScriptLengthLimits_ShouldBeEnforced()
    {
        var commands = string.Join(';', Enumerable.Repeat("Save()", LyricDrawScriptParser.MaximumCommandCount + 1));
        _parser.Parse(commands).IsSuccess.Should().BeFalse();
        _parser.Parse(new string('x', LyricDrawScriptParser.MaximumScriptLength + 1)).IsSuccess.Should().BeFalse();
        await Task.CompletedTask;
    }
}
