using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Npgsql.Schema;
using Npgsql;
using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.Data;
using Humanizer;
using Squeel.Diagnostics;
using System.Text.Json;

namespace Squeel.Generators;

[Generator(LanguageNames.CSharp)]
public sealed class PostgresGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        RegisterInterceptorsForQueries(context);
        RegisterInterceptorsForExecutes(context);
    }

    private static readonly SymbolDisplayFormat _checkFormat = new(
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
        genericsOptions: SymbolDisplayGenericsOptions.None,
        memberOptions: SymbolDisplayMemberOptions.IncludeContainingType,
        extensionMethodStyle: SymbolDisplayExtensionMethodStyle.StaticMethod,
        parameterOptions: SymbolDisplayParameterOptions.IncludeType
        );

    private static void RegisterInterceptorsForQueries(IncrementalGeneratorInitializationContext context)
    {
        context.RegisterSourceOutput(context.SyntaxProvider
            .CreateSyntaxProvider(Filter, Transform)
            .Where(info =>
            {
                var invocationSymbol = info.SemanticModel.GetSymbolInfo(info.Invocation);
                var check = invocationSymbol.Symbol?.ToDisplayString(_checkFormat);
                return check is "Squeel.SqueelExtensions.QueryAsync";
            })
            .Combine(context.AdditionalTextsProvider.Collect())
            .Combine(context.CompilationProvider)
            .Select(static (info, ct) =>
            {
                var argument = info.Left.Left.Invocation.ArgumentList.Arguments[0];
                var interpolatedQuery = (InterpolatedStringExpressionSyntax)argument.Expression;
                var sql = ToParameterizedString(interpolatedQuery, out var parameters);
                var member = (MemberAccessExpressionSyntax)info.Left.Left.Invocation.Expression;
                var name = (GenericNameSyntax)member.Name;
                var type = name.TypeArgumentList.Arguments[0];
                var entity = type switch
                {
                    IdentifierNameSyntax id => id.Identifier.ValueText,
                    PredefinedTypeSyntax pt => pt.Keyword.ValueText,
                    _ => throw new NotSupportedException($"TypeSyntax {type.GetType().Name} is not supported"),
                };
                var p = parameters.Select(p => new SqlParameterDescriptor(p.Key, ToExampleValue(info.Left.Left.SemanticModel.GetTypeInfo(p.Value)))).ToImmutableArray();
                var squeelJsonFile = info.Left.Right.FirstOrDefault(at => Path.GetFileName(at.Path).Equals("squeel.json", StringComparison.OrdinalIgnoreCase));
                var squeelJsonContent = squeelJsonFile?.GetText(ct)?.ToString();
                if (string.IsNullOrWhiteSpace(squeelJsonContent))
                    throw new InvalidOperationException("Missing squeel.json content");
                var squeelJson = JsonDocument.Parse(squeelJsonContent!);
                var connectionstring = squeelJson.RootElement.GetProperty("connectionstring"u8).GetString();
                return new QueryCall
                {
                    ConnectionString = connectionstring,
                    Name = entity,
                    PredefinedType = type is PredefinedTypeSyntax,
                    Sql = sql,
                    Parameters = p,
                    GetLocation = () => interpolatedQuery.SyntaxTree.GetLocation(interpolatedQuery.Span),
                    InterceptorPath = $"@\"{info.Right.Options.SourceReferenceResolver?.NormalizePath(member.SyntaxTree.FilePath, baseFilePath: null) ?? member.SyntaxTree.FilePath}\"",
                    InterceptorLine = member.Name.GetLocation().GetLineSpan().Span.Start.Line + 1,
                    InterceptorColumn = member.Name.GetLocation().GetLineSpan().Span.Start.Character + 1,
                };
            })
            .WithComparer(_queryCallComparer), GenerateQueryInterceptor);
    }

    private static readonly QueryCallComparer _queryCallComparer = new();

    private static void RegisterInterceptorsForExecutes(IncrementalGeneratorInitializationContext context)
    {
        context.RegisterSourceOutput(context.SyntaxProvider
            .CreateSyntaxProvider(Filter, Transform)
            .Where(info =>
            {
                var invocationSymbol = info.SemanticModel.GetSymbolInfo(info.Invocation);
                var check = invocationSymbol.Symbol?.ToDisplayString(_checkFormat);
                return check is "Squeel.SqueelExtensions.ExecuteAsync";
            })
            .Combine(context.AdditionalTextsProvider.Collect())
            .Combine(context.CompilationProvider)
            .Select(static (info, ct) =>
            {
                var argument = info.Left.Left.Invocation.ArgumentList.Arguments[0];
                var interpolatedQuery = (InterpolatedStringExpressionSyntax)argument.Expression;
                var sql = ToParameterizedString(interpolatedQuery, out var parameters);
                var member = (MemberAccessExpressionSyntax)info.Left.Left.Invocation.Expression;
                var p = parameters.Select(p => new SqlParameterDescriptor(p.Key, ToExampleValue(info.Left.Left.SemanticModel.GetTypeInfo(p.Value)))).ToImmutableArray();
                var squeelJsonFile = info.Left.Right.FirstOrDefault(at => Path.GetFileName(at.Path).Equals("squeel.json", StringComparison.OrdinalIgnoreCase));
                var squeelJsonContent = squeelJsonFile?.GetText(ct)?.ToString();
                if (string.IsNullOrWhiteSpace(squeelJsonContent))
                    throw new InvalidOperationException("Missing squeel.json content");
                var squeelJson = JsonDocument.Parse(squeelJsonContent!);
                var connectionstring = squeelJson.RootElement.GetProperty("connectionstring"u8).GetString();
                return new ExecuteCall
                {
                    ConnectionString = connectionstring,
                    Sql = sql,
                    Parameters = p,
                    GetLocation = () => interpolatedQuery.SyntaxTree.GetLocation(interpolatedQuery.Span),
                    InterceptorPath = $"@\"{info.Right.Options.SourceReferenceResolver?.NormalizePath(member.SyntaxTree.FilePath, baseFilePath: null) ?? member.SyntaxTree.FilePath}\"",
                    InterceptorLine = member.Name.GetLocation().GetLineSpan().Span.Start.Line + 1,
                    InterceptorColumn = member.Name.GetLocation().GetLineSpan().Span.Start.Character + 1,
                };
            })
            .WithComparer(new ExecuteCallComparer()), GenerateExecuteInterceptor);
    }

    private static void GenerateExecuteInterceptor(SourceProductionContext context, ExecuteCall entity)
    {
        try
        {
            using var connection = new NpgsqlConnection(entity.ConnectionString);
            connection.Open();
            using var transaction = connection.BeginTransaction();
            try
            {
                using var command = connection.CreateCommand();
                command.CommandText = entity.Sql;
                foreach (var p in entity.Parameters)
                {
                    command.Parameters.Add(new NpgsqlParameter(p.Name, p.Value));
                }
                using var reader = command.ExecuteReader(CommandBehavior.SingleResult);
            }
            finally
            {
                transaction.Rollback();
            }

            context.AddSource($"Execute-{Guid.NewGuid()}.g.cs", $$"""
                {{GeneratedFileOptions.Header}}

                #pragma warning disable CA2100

                namespace System.Runtime.CompilerServices
                {
                #pragma warning disable CS9113
                    {{GeneratedFileOptions.Attribute}}
                    [global::System.AttributeUsage(global::System.AttributeTargets.Method, AllowMultiple = true)]
                    file sealed class InterceptsLocationAttribute(string filePath, int line, int character) : global::System.Attribute{}
                #pragma warning restore CS9113
                }

                namespace Squeel
                {
                    {{GeneratedFileOptions.Attribute}}
                    file static class ExecuteImplementation
                    {
                        {{GeneratedFileOptions.Attribute}}
                        [global::System.Runtime.CompilerServices.InterceptsLocation({{entity.InterceptorPath}}, {{entity.InterceptorLine}}, {{entity.InterceptorColumn}})]
                        public static global::System.Threading.Tasks.Task<int>
                            ExecuteAsync
                        (
                            this global::System.Data.Common.DbConnection connection,
                            ref global::Squeel.SqueelInterpolatedStringHandler query,
                            global::System.Threading.CancellationToken ct = default
                        )
                        {
                            var conn = (global::Npgsql.NpgsqlConnection)connection;
                            var sql = query.ToString('@');
                
                            using var command = conn.CreateCommand();
                            command.CommandText = sql;
                            return __Exec(command, query.Parameters, ct);
                
                            static async global::System.Threading.Tasks.Task<int> __Exec
                            (
                                global::Npgsql.NpgsqlCommand command,
                                global::System.Collections.Generic.IEnumerable<global::Squeel.ParameterDescriptor> parameters,
                                global::System.Threading.CancellationToken ct
                            )
                            {
                                foreach (var pd in parameters)
                                {
                                    var p = command.CreateParameter();
                                    p.ParameterName = pd.Name;
                                    p.Value = pd.Value;
                                    command.Parameters.Add(p);
                                }
                
                                return await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                            }
                        }
                    }
                }
                """);
        }
        catch (PostgresException sql)
        {
            context.ReportQueryValidationFailed(entity.GetLocation(), "postgres", sql.MessageText);
        }
    }

    private static bool Filter(SyntaxNode node, CancellationToken ct)
        => node is InvocationExpressionSyntax;

    private static SqueelCallSite Transform(GeneratorSyntaxContext context, CancellationToken ct)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;

        return new()
        {
            Invocation = invocation,
            SemanticModel = context.SemanticModel,
        };
    }

    private readonly record struct SqlParameterDescriptor(string Name, object? Value);

    private sealed class QueryCallComparer : IEqualityComparer<QueryCall>
    {
        public bool Equals(QueryCall x, QueryCall y)
            => x.Name == y.Name
            && x.Sql == y.Sql
            && x.Parameters.SequenceEqual(y.Parameters)
            && x.ConnectionString == y.ConnectionString
            && x.InterceptorPath == y.InterceptorPath
            && x.InterceptorLine == y.InterceptorLine
            && x.InterceptorColumn == y.InterceptorColumn;

        public int GetHashCode(QueryCall obj) => HashCode.Combine(
            obj.Name,
            obj.Sql,
            obj.Parameters,
            obj.ConnectionString,
            obj.InterceptorPath,
            obj.InterceptorLine,
            obj.InterceptorColumn);
    }

    private sealed class ExecuteCallComparer : IEqualityComparer<ExecuteCall>
    {
        public bool Equals(ExecuteCall x, ExecuteCall y)
            => x.Sql == y.Sql
            && x.Parameters.SequenceEqual(y.Parameters)
            && x.ConnectionString == y.ConnectionString
            && x.InterceptorPath == y.InterceptorPath
            && x.InterceptorLine == y.InterceptorLine
            && x.InterceptorColumn == y.InterceptorColumn;

        public int GetHashCode(ExecuteCall obj) => HashCode.Combine(
            obj.Sql,
            obj.Parameters,
            obj.ConnectionString,
            obj.InterceptorPath,
            obj.InterceptorLine,
            obj.InterceptorColumn);
    }

    private readonly record struct QueryCall
    {
        public required string Name { get; init; }
        public required string Sql { get; init; }
        public required bool PredefinedType { get; init; }
        public required ImmutableArray<SqlParameterDescriptor> Parameters { get; init; }
        public required string? ConnectionString { get; init; }
        public required string InterceptorPath { get; init; }
        public required int InterceptorLine { get; init; }
        public required int InterceptorColumn { get; init; }
        internal required Func<Location> GetLocation { get; init; }
    }

    private readonly record struct ExecuteCall
    {
        public required string Sql { get; init; }
        public required ImmutableArray<SqlParameterDescriptor> Parameters { get; init; }
        public required string? ConnectionString { get; init; }
        public required string InterceptorPath { get; init; }
        public required int InterceptorLine { get; init; }
        public required int InterceptorColumn { get; init; }
        internal required Func<Location> GetLocation { get; init; }
    }

    private static ReadOnlyCollection<NpgsqlDbColumn> GetColumnSchema(
        NpgsqlConnection connection,
        string sql,
        in ImmutableArray<SqlParameterDescriptor> parameters)
    {
        using var schemaCommand = connection.CreateCommand();
        schemaCommand.CommandText = sql;
        foreach (var p in parameters)
        {
            schemaCommand.Parameters.Add(new NpgsqlParameter(p.Name, p.Value));
        }
        using var schemaReader = schemaCommand.ExecuteReader(CommandBehavior.SchemaOnly | CommandBehavior.SingleResult | CommandBehavior.KeyInfo);
        return schemaReader.GetColumnSchema();
    }

    private static void GenerateQueryInterceptor(SourceProductionContext context, QueryCall entity)
    {
        try
        {
            using var connection = new NpgsqlConnection(entity.ConnectionString);
            connection.Open();
            using var transaction = connection.BeginTransaction();
            try
            {
                var columns = GetColumnSchema(connection, entity.Sql, entity.Parameters);

                if (!entity.PredefinedType)
                    GenerateEntity(context, entity, columns);
                GenerateInterceptor(context, entity, columns);
            }
            finally
            {
                transaction.Rollback();
            }
        }
        catch (PostgresException sql)
        {
            if (entity.PredefinedType)
            {
                context.AddSource($"{entity.Name}-{Guid.NewGuid()}.g.cs", $$"""
                    {{GeneratedFileOptions.Header}}
                    
                    namespace Squeel;
                    
                    // Code omitted because SQL query failed
                    /*
                        {{sql.Message}}
                    */
                    """);
            }
            else
            {
                context.AddSource($"{entity.Name}.g.cs", $$"""
                    {{GeneratedFileOptions.Header}}

                    namespace Squeel;

                    {{GeneratedFileOptions.Attribute}}
                    internal sealed record {{entity.Name}}
                    {
                        // Code omitted because SQL query failed
                        /*
                            {{sql.Message}}
                        */
                    }
                    """);
            }

            context.ReportQueryValidationFailed(entity.GetLocation(), "postgres", sql.MessageText);
        }
    }

    private static string ParseFromReader(NpgsqlDbColumn column)
    {
        var ordinality = column.ColumnOrdinal!.Value;
        var propertyName = column.ColumnName.Pascalize();
        var propertyType = column.DataType!.FullName;

        if (column.AllowDBNull is false)
            return $"{propertyName} = await reader.GetFieldValueAsync<global::{propertyType}>({ordinality}, ct).ConfigureAwait(false),";

        return $"{propertyName} = await reader.IsDBNullAsync({ordinality}, ct) ? null : await reader.GetFieldValueAsync<global::{propertyType}{Nullability(column)}>({ordinality}, ct).ConfigureAwait(false),";
    }

    private static string Nullability(NpgsqlDbColumn c)
    {
        if (c.AllowDBNull is true or null)
            return "?";
        else
            return string.Empty;
    }

    private static string Requiredness(NpgsqlDbColumn c)
    {
        if (c.DefaultValue is not null)
            return "";

        return " required";
    }

    private static string InterceptsLocationAttributeDefinition => $$"""
        namespace System.Runtime.CompilerServices
        {
        #pragma warning disable CS9113
            {{GeneratedFileOptions.Attribute}}
            [global::System.AttributeUsage(global::System.AttributeTargets.Method, AllowMultiple = true)]
            file sealed class InterceptsLocationAttribute(string filePath, int line, int character) : global::System.Attribute{}
        #pragma warning restore CS9113
        }
        """;

    static string JoinAndIndent(int indent, int skip, IEnumerable<string> lines)
    {
        return string.Join("\n", lines.Select((l, i) =>
        {
            if (i >= skip)
                return $"{new string(' ', indent)}{l}";
            else
                return l;
        }));
    }

    private static string GenerateEntityProperty(NpgsqlDbColumn c)
        => $"public{Requiredness(c)} global::{c.DataType!.FullName}{Nullability(c)} {c.ColumnName.Pascalize()} {{ get; init; }}";

    private static void GenerateEntity(in SourceProductionContext context, QueryCall entity, ReadOnlyCollection<NpgsqlDbColumn> columns)
    {
        context.AddSource($"{entity.Name}.g.cs", $$""""
            {{GeneratedFileOptions.Header}}

            #nullable enable

            namespace Squeel;

            {{GeneratedFileOptions.Attribute}}
            internal sealed record {{entity.Name}}
            {
                {{JoinAndIndent(4, 1, columns.Select(GenerateEntityProperty))}}
            }
            """");
    }

    private static void GenerateInterceptor(in SourceProductionContext context, QueryCall entity, ReadOnlyCollection<NpgsqlDbColumn> columns)
    {
        var entityParser = entity.PredefinedType ? $$"""
            await reader.GetFieldValueAsync<{{entity.Name}}>(0, ct).ConfigureAwait(false)
            """
            : $$"""
            new {{entity.Name}}
            {
                {{JoinAndIndent(24, 1, columns.Select(ParseFromReader))}}
            }
            """;
        context.AddSource($"{entity.Name}Interceptor.g.cs", $$""""
            {{GeneratedFileOptions.Header}}

            #nullable enable

            #pragma warning disable CA2100

            {{InterceptsLocationAttributeDefinition}}

            namespace Squeel
            {
                {{GeneratedFileOptions.Attribute}}
                internal static class {{entity.Name}}QueryImplementation
                {
                    {{GeneratedFileOptions.Attribute}}
                    [global::System.Runtime.CompilerServices.InterceptsLocation({{entity.InterceptorPath}}, {{entity.InterceptorLine}}, {{entity.InterceptorColumn}})]
                    public static global::System.Threading.Tasks.Task<global::System.Collections.Generic.IEnumerable<{{entity.Name}}>>
                        QueryAsync__{{entity.Name}}
                    (
                        this global::System.Data.Common.DbConnection connection,
                        ref global::Squeel.SqueelInterpolatedStringHandler query,
                        global::System.Threading.CancellationToken ct = default
                    )
                    {
                        var conn = (global::Npgsql.NpgsqlConnection)connection;
                        var sql = query.ToString('@');

                        using var command = conn.CreateCommand();
                        command.CommandText = sql;
                        return __Exec(command, query.Parameters, ct);

                        static async global::System.Threading.Tasks.Task<global::System.Collections.Generic.IEnumerable<{{entity.Name}}>>
                        __Exec
                        (
                            global::Npgsql.NpgsqlCommand command,
                            global::System.Collections.Generic.IEnumerable<global::Squeel.ParameterDescriptor> parameters,
                            global::System.Threading.CancellationToken ct
                        )
                        {
                            foreach (var pd in parameters)
                            {
                                var p = command.CreateParameter();
                                p.ParameterName = pd.Name;
                                p.Value = pd.Value;
                                command.Parameters.Add(p);
                            }

                            var flags = global::System.Data.CommandBehavior.SequentialAccess | global::System.Data.CommandBehavior.SingleResult;
                            using var reader = await command.ExecuteReaderAsync(flags, ct).ConfigureAwait(false);

                            var list = new global::System.Collections.Generic.List<{{entity.Name}}>();
                            while (await reader.ReadAsync(ct).ConfigureAwait(false))
                            {
                                list.Add({{entityParser}});
                            }
                            return list;
                        }
                    }
                }
            }
            """");
    }

    internal static string ToParameterizedString(InterpolatedStringExpressionSyntax interpolated, out Dictionary<string, IdentifierNameSyntax> parameters)
    {
        var sql = new StringBuilder();
        parameters = new Dictionary<string, IdentifierNameSyntax>(interpolated.Contents.Count);
        foreach (var content in interpolated.Contents)
        {
            if (content is InterpolatedStringTextSyntax text)
            {
                sql.Append(text.TextToken.ValueText);
            }
            else if (content is InterpolationSyntax { Expression: IdentifierNameSyntax name })
            {
                sql.Append($"@{name.Identifier.ValueText}");
                parameters.Add(name.Identifier.ValueText, name);
            }
        }

        return sql.ToString();
    }

    private static object ToExampleValue(TypeInfo typeInfo)
    {
        return typeInfo.Type?.Name switch
        {
            "Int32" => -32,
            "String" => "Hello World",
            "Boolean" => true,
            "DateTime" => new DateTime(2000, 1, 1),
            "Decimal" => 6.4m,
            "Double" => 3.2d,
            "Single" => 3.2f,
            "Int16" => -16,
            "Int64" => -64,
            "Byte" => -8,
            "SByte" => 8,
            "UInt16" => 16,
            "UInt32" => 32,
            "UInt64" => 64,
            "Char" => 'a',
            "Guid" => Guid.NewGuid(),
            "TimeSpan" => TimeSpan.FromTicks(100),
            "DateTimeOffset" => new DateTimeOffset(new DateTime(2000, 1, 1)),
            //"Nullable`1" => ToRandomizedValue(),
            _ => throw new NotSupportedException($"Type {typeInfo.Type?.Name} is not supported"),
        };
    }
}