using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using SpocR.SpocRVNext.Models;
using Xunit.Abstractions;

namespace SpocR.TestFramework;

/// <summary>
/// Lightweight SQL Server test base that reuses the StoredProcedureContentModel parser
/// to validate SQL definitions without requiring a live database connection.
/// </summary>
public abstract class SqlServerTestBase : SpocRTestBase
{
    private static readonly Regex ProcedureHeader = new(
        @"CREATE\s+PROCEDURE\s+(?:(?<schema>\[[^\]]+\]|[A-Za-z0-9_]+)\.)?(?<name>\[[^\]]+\]|[A-Za-z0-9_]+)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    protected SqlServerTestBase(ITestOutputHelper output)
    {
        Output = output ?? throw new ArgumentNullException(nameof(output));
    }

    protected ITestOutputHelper Output { get; }

    protected Task<SqlAnalysisResult> ExecuteSqlAndAnalyzeAsync(string sql, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sql))
        {
            throw new ArgumentException("SQL definition must not be null or empty.", nameof(sql));
        }

        cancellationToken.ThrowIfCancellationRequested();

        var descriptor = ExtractDescriptor(sql);
        var content = StoredProcedureContentModel.Parse(sql, descriptor.Schema);

        var analysis = new ProcedureAnalysis(descriptor.Schema, descriptor.Name, content);
        var result = new SqlAnalysisResult();
        result.Procedures.Add(analysis);

        return Task.FromResult(result);
    }

    private static (string Schema, string Name) ExtractDescriptor(string sql)
    {
        var match = ProcedureHeader.Match(sql);
        if (!match.Success)
        {
            throw new InvalidOperationException("Unable to extract procedure name from SQL definition.");
        }

        string Unwrap(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return value;
            }

            return value.Trim().Trim('[', ']', '"');
        }

        var schema = Unwrap(match.Groups["schema"].Value);
        if (string.IsNullOrWhiteSpace(schema))
        {
            schema = "dbo";
        }

        var name = Unwrap(match.Groups["name"].Value);
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new InvalidOperationException("Procedure name could not be resolved from SQL definition.");
        }

        return (schema, name);
    }

    public sealed class SqlAnalysisResult
    {
        public List<ProcedureAnalysis> Procedures { get; } = new();
    }

    public sealed class ProcedureAnalysis
    {
        public ProcedureAnalysis(string schema, string name, StoredProcedureContentModel content)
        {
            Schema = schema;
            Name = name;
            Content = content ?? throw new ArgumentNullException(nameof(content));
        }

        public string Schema { get; }
        public string Name { get; }
        public StoredProcedureContentModel Content { get; }
        public IReadOnlyList<StoredProcedureContentModel.ResultSet> ResultSets => Content.ResultSets;
    }
}
