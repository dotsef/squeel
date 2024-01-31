using System.Collections.Immutable;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Squeel.Tests.Postgres;

public readonly record struct SqueelTestResult
{
    public required ImmutableArray<Diagnostic> Errors { get; init; }

    public required ImmutableArray<SyntaxTree> GeneratedFiles { get; init; }

    public required ImmutableArray<Diagnostic> GeneratorDiagnostics { get; init; }

    public required AnalysisResult AnalyzerDiagnostics { get; init; }
}