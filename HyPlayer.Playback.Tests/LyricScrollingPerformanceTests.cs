using HyPlayer.LyricEffects.Expressions;
using HyPlayer.LyricEffects.Models;
using HyPlayer.LyricRenderer.Pipeline;
using TUnit.Core;

namespace HyPlayer.Playback.Tests;

public sealed class LyricScrollingPerformanceTests
{
    [Test]
    public void ConstantWholeLineParameter_ShouldBeFoldedBeforeRenderingFrames()
    {
        var compiler = new CountingExpressionCompiler(FocusedTextExpressionDependencies.None);
        var parameter = CompileScalar(compiler);
        var runtime = parameter.CreateRuntime();
        using var resources = new LyricRenderFrameResourceScope();
        var context = CreateContext(resources);

        for (var frame = 0; frame < 100; frame++)
            _ = runtime.Evaluate(context);

        if (compiler.ScalarEvaluationCount > 1)
            throw new InvalidOperationException(
                $"常量整体表达式在 100 次渲染求值中执行了 {compiler.ScalarEvaluationCount} 次，应当在编译阶段至多执行一次。");
    }

    [Test]
    public void LineDependentWholeLineParameter_ShouldRemainDynamic()
    {
        var compiler = new CountingExpressionCompiler(FocusedTextExpressionDependencies.Line);
        var parameter = CompileScalar(compiler);
        var runtime = parameter.CreateRuntime();
        using var resources = new LyricRenderFrameResourceScope();
        var context = CreateContext(resources);

        for (var frame = 0; frame < 10; frame++)
            _ = runtime.Evaluate(context);

        if (compiler.ScalarEvaluationCount != 10)
            throw new InvalidOperationException(
                $"依赖 line 的整体表达式应保持动态，实际执行了 {compiler.ScalarEvaluationCount} 次。");
    }

    private static CompiledScalarParameter CompileScalar(ILyricExpressionCompiler compiler)
    {
        var diagnostics = new List<LyricProfileDiagnostic>();
        var result = LyricOperationCompilerHelpers.CompileScalar(
            compiler,
            new LyricRenderOperationDefinition
            {
                InstanceId = "performance-test",
                Parameters =
                {
                    ["value"] = new LyricOperationParameterDefinition { Expression = "1" }
                }
            },
            new LyricOperationParameterDescriptor
            {
                Key = "value",
                DisplayName = "Value",
                ValueType = LyricExpressionValueType.Scalar,
                DefaultExpression = "1"
            },
            diagnostics);

        return result ?? throw new InvalidOperationException(string.Join(Environment.NewLine, diagnostics.Select(x => x.Message)));
    }

    private static LyricRenderOperationContext CreateContext(LyricRenderFrameResourceScope resources)
    {
        var sample = LyricExpressionSamples.All[0];
        return new LyricRenderOperationContext
        {
            SourceImage = null!,
            TargetSession = null!,
            Resources = resources,
            Line = sample.Line,
            Frame = sample.Frame,
            OffsetX = 0,
            OffsetY = 0,
            DebugEnabled = false
        };
    }

    private sealed class CountingExpressionCompiler(FocusedTextExpressionDependencies dependencies)
        : ILyricExpressionCompiler
    {
        public int ScalarEvaluationCount { get; private set; }

        public LyricExpressionCompileResult<LyricScalarExpression> CompileScalar(string source) =>
            LyricExpressionCompileResult<LyricScalarExpression>.Success(
                (_, _, _) =>
                {
                    ScalarEvaluationCount++;
                    return 1;
                },
                dependencies);

        public LyricExpressionCompileResult<LyricColorExpression> CompileColor(string source) =>
            throw new NotSupportedException();

        public LyricExpressionCompileResult<LyricTextExpression> CompileText(string source) =>
            throw new NotSupportedException();

        public LyricExpressionCompileResult<FocusedTextScalarExpression> CompileFocusedScalar(string source) =>
            throw new NotSupportedException();

        public LyricExpressionCompileResult<FocusedTextColorExpression> CompileFocusedColor(string source) =>
            throw new NotSupportedException();

        public LyricExpressionCompileResult<FocusedTextTextExpression> CompileFocusedText(string source) =>
            throw new NotSupportedException();
    }
}
