using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Squeel.Diagnostics;

public static partial class SqueelDescriptors
{
    public static readonly DiagnosticDescriptor MissingSqueelConnectionString = new(
        id: "SQUEEL001",
        title: "Missing connectionstring in squeel.json",
        messageFormat: "Missing connectionstring in squeel.json",
        category: "Squeel",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        helpLinkUri: "",
        customTags: WellKnownDiagnosticTags.CompilationEnd);
}

public static partial class SqueelDiagnostics
{
    public static Diagnostic MissingSqueelConnectionString()
    {
        return Diagnostic.Create(SqueelDescriptors.MissingSqueelConnectionString, Location.None);
    }

    public static void ReportMissingSqueelConnectionString(this CompilationAnalysisContext context)
    {
        context.ReportDiagnostic(MissingSqueelConnectionString());
    }
}