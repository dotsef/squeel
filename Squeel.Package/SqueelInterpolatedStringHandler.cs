using System.Runtime.CompilerServices;

#pragma warning disable CA1815

namespace Squeel;

public readonly record struct ParameterDescriptor
{
    public required Type Type { get; init; }

    public required object? Value { get; init; }

    public required string Name { get; init; }
}

[InterpolatedStringHandler]
public readonly struct SqueelInterpolatedStringHandler
{
    private readonly int _literalLength;
    private readonly int _formattedCount;

    private readonly List<ParameterDescriptor> _parameters = [];

    public IEnumerable<ParameterDescriptor> Parameters => _parameters;

    private readonly List<Func<char, string>> _appenders = [];

    public SqueelInterpolatedStringHandler(int literalLength, int formattedCount, out bool shouldAppend)
    {
        shouldAppend = true;
        _literalLength = literalLength;
        _formattedCount = formattedCount;
    }

    public void AppendLiteral(string literal)
        => _appenders.Add(p => literal);

    public void AppendFormatted<T>(
        T value,
        [CallerArgumentExpression(nameof(value))] string expression = "")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(expression);

        var parameterName = $"{expression.Split('.').Last()}";
        _parameters.Add(new ParameterDescriptor
        {
            Name = parameterName,
            Value = value,
            Type = typeof(T)
        });
        _appenders.Add(p => $"{p}{parameterName}");
    }

    public string ToString(char parameterPrefix)
    {
        var handler = new DefaultInterpolatedStringHandler(_literalLength, _formattedCount);

        foreach (var appender in _appenders)
            handler.AppendLiteral(appender(parameterPrefix));

        return handler.ToString();
    }
}