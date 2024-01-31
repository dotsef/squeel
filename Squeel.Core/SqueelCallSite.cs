using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Squeel;

public sealed record SqueelCallSite
{
    public required InvocationExpressionSyntax Invocation { get; init; }

    public required SemanticModel SemanticModel { get; init; }
}
