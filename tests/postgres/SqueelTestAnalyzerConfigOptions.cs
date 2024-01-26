using System.Diagnostics.CodeAnalysis;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Squeel.Tests.Postgres;

internal sealed class SqueelTestAnalyzerConfigOptions : AnalyzerConfigOptions
{
    private readonly string _connectionString;

    public SqueelTestAnalyzerConfigOptions(string connectionString)
    {
        _connectionString = connectionString;
    }

    public override bool TryGetValue(string key, [NotNullWhen(true)] out string? value)
    {
        if (key is not "build_property.__SqueelConnectionString")
        {
            value = null;
            return false;
        }

        value = _connectionString;
        return true;
    }
}

internal sealed class SqueelTestAnalyzerConfigOptionsProvider : AnalyzerConfigOptionsProvider
{
    public SqueelTestAnalyzerConfigOptionsProvider(string connectionString)
    {
        GlobalOptions = new SqueelTestAnalyzerConfigOptions(connectionString);
    }

    public override AnalyzerConfigOptions GlobalOptions { get; }

    public override AnalyzerConfigOptions GetOptions(SyntaxTree tree)
    {
        return GlobalOptions;
    }

    public override AnalyzerConfigOptions GetOptions(AdditionalText textFile)
    {
        return GlobalOptions;
    }
}