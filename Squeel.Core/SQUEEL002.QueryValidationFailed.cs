using Microsoft.CodeAnalysis;

namespace Squeel.Diagnostics;

public static partial class SqueelDescriptors
{
    public static readonly DiagnosticDescriptor QueryValidationFailed = new(
        id: "SQUEEL002",
        title: "Query validation failed",
        messageFormat: "{0}: {1}",
        category: "Squeel",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "",
        helpLinkUri: "",
        customTags: []);
}

public static partial class SqueelDiagnostics
{
    public static Diagnostic QueryValidationFailed(Location location, string db, string message)
    {
        return Diagnostic.Create(SqueelDescriptors.QueryValidationFailed, location, db, message);
    }

    public static void ReportQueryValidationFailed(this SourceProductionContext context, Location location, string type, string sql)
    {
        context.ReportDiagnostic(QueryValidationFailed(location, "postgres", sql));
    }
}