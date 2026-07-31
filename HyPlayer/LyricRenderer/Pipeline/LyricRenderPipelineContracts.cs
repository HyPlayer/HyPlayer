using System.Collections.Generic;
using System.Linq;

namespace HyPlayer.LyricRenderer.Pipeline;

public enum LyricProfileDiagnosticSeverity
{
    Warning,
    Error
}

public sealed record LyricProfileDiagnostic(
    LyricProfileDiagnosticSeverity Severity,
    string Message,
    string? InstanceId = null,
    string? Parameter = null,
    int Line = 0,
    int Column = 0);

public sealed class LyricProfileCompileResult
{
    public required IReadOnlyList<LyricProfileDiagnostic> Diagnostics { get; init; }

    public CompiledLyricEffectProfile? Profile { get; init; }

    public bool IsSuccess => Profile is not null &&
                             Diagnostics.All(item => item.Severity != LyricProfileDiagnosticSeverity.Error);
}

public sealed class LyricOperationCompileResult
{
    public CompiledLyricRenderOperation? Operation { get; init; }

    public IReadOnlyList<LyricProfileDiagnostic> Diagnostics { get; init; } = [];

    public bool IsSuccess => Operation is not null &&
                             Diagnostics.All(item => item.Severity != LyricProfileDiagnosticSeverity.Error);
}
