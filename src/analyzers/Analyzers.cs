using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;

using Squeel.Diagnostics;

namespace Squeel;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class SqueelConnectionStringAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = [SqueelDescriptors.MissingSqueelConnectionString];

    public override void Initialize([NotNull] AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.Analyze | GeneratedCodeAnalysisFlags.ReportDiagnostics);

        context.RegisterCompilationAction(compilationContext =>
        {
            if (!compilationContext.Options.AnalyzerConfigOptionsProvider.GlobalOptions.TryGetValue("build_property.__SqueelConnectionString", out var connectionString) || string.IsNullOrWhiteSpace(connectionString))
                compilationContext.ReportMissingSqueelConnectionString();
        });
    }
}