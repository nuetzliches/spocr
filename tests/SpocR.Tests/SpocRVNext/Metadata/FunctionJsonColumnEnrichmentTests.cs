using System;
using System.Collections.Generic;
using System.Dynamic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using SpocR.Services; // FunctionSnapshotCollector + Snapshot models
using SpocR.SpocRVNext.Data; // DbContext base
using SpocR.SpocRVNext.Data.Queries; // FunctionRow / ParamRow models
using Microsoft.Data.SqlClient;

namespace SpocR.Tests.SpocRVNext.Metadata;

public class FunctionJsonColumnEnrichmentTests
{
    [Fact]
    public async Task RecordAsJson_FunctionColumns_Should_Be_Enriched_With_Types()
    {
        // Arrange: fake DbContext returning one scalar JSON function with parameters
        var fake = new FakeDbContext();
        var layout = new SchemaSnapshotFileLayoutService();
        var console = new NullConsoleService();
        var collector = new FunctionSnapshotCollector(fake, layout, console);
        var snapshot = new SchemaSnapshot
        {
            Tables = new List<SnapshotTable>
            {
                new SnapshotTable
                {
                    Schema = "identity",
                    Name = "User",
                    Columns = new List<SnapshotTableColumn>
                    {
                        new SnapshotTableColumn { Name = "UserName", TypeRef = "sys.nvarchar", MaxLength = 200, IsNullable = false },
                        new SnapshotTableColumn { Name = "Initials", TypeRef = "sys.nvarchar", MaxLength = 10, IsNullable = false }
                    }
                }
            }
        };

        // Act
        await collector.CollectAsync(snapshot, CancellationToken.None);
        var fn = snapshot.Functions.Single(f => f.Name == "RecordAsJson" && f.Schema == "identity");

        // Assert base JSON flags
        Assert.True(fn.ReturnsJson); // AST should mark FOR JSON PATH
        Assert.False(fn.ReturnsJsonArray.GetValueOrDefault());
        Assert.NotNull(fn.Columns);
        // Build lookup for quick assertions
        var colMap = fn.Columns.ToDictionary(c => c.Name, c => c, StringComparer.OrdinalIgnoreCase);

        // recordId -> from parameter RecordId (int)
        Assert.True(colMap.ContainsKey("recordId"));
        Assert.Equal("sys.int", colMap["recordId"].TypeRef);

        // rowVersion -> from parameter RowVersion (bigint) (enrichment prefers param over fallback binary(8))
        Assert.True(colMap.ContainsKey("rowVersion"));
        Assert.Equal("sys.bigint", colMap["rowVersion"].TypeRef);

        // created.user.userId -> suffix match CreatedUserId (int)
        Assert.True(colMap.ContainsKey("created.user.userId"));
        Assert.Equal("core._id", colMap["created.user.userId"].TypeRef);

        // updated.user.userId -> UpdatedUserId (int)
        Assert.True(colMap.ContainsKey("updated.user.userId"));
        Assert.Equal("sys.int", colMap["updated.user.userId"].TypeRef);

        // created.user.displayName -> table mapping UserName
        Assert.True(colMap.ContainsKey("created.user.displayName"));
        Assert.Equal("sys.nvarchar", colMap["created.user.displayName"].TypeRef);
        Assert.Equal(200, colMap["created.user.displayName"].MaxLength);

        // created.user.initials -> table mapping Initials
        Assert.True(colMap.ContainsKey("created.user.initials"));
        Assert.Equal("sys.nvarchar", colMap["created.user.initials"].TypeRef);
        Assert.Equal(10, colMap["created.user.initials"].MaxLength);

        // updated.user.displayName -> also mapped
        Assert.True(colMap.ContainsKey("updated.user.displayName"));
        Assert.Equal("sys.nvarchar", colMap["updated.user.displayName"].TypeRef);
        Assert.Equal(200, colMap["updated.user.displayName"].MaxLength);

        // updated.user.initials -> mapped
        Assert.True(colMap.ContainsKey("updated.user.initials"));
        Assert.Equal("sys.nvarchar", colMap["updated.user.initials"].TypeRef);
        Assert.Equal(10, colMap["updated.user.initials"].MaxLength);
    }

    [Fact]
    public async Task RecordAsJson_FunctionParameters_Should_Fallback_To_UserDefinedType_Nullability()
    {
        // Arrange
        var fake = new FakeDbContext();
        var layout = new SchemaSnapshotFileLayoutService();
        var console = new NullConsoleService();
        var collector = new FunctionSnapshotCollector(fake, layout, console);
        var snapshot = new SchemaSnapshot
        {
            Tables = new List<SnapshotTable>
            {
                new SnapshotTable
                {
                    Schema = "identity",
                    Name = "User",
                    Columns = new List<SnapshotTableColumn>
                    {
                        new SnapshotTableColumn { Name = "UserName", TypeRef = "sys.nvarchar", MaxLength = 200, IsNullable = false },
                        new SnapshotTableColumn { Name = "Initials", TypeRef = "sys.nvarchar", MaxLength = 10, IsNullable = false }
                    }
                }
            }
        };

        // Act
        await collector.CollectAsync(snapshot, CancellationToken.None);
        var fn = snapshot.Functions.Single(f => f.Name == "RecordAsJson" && f.Schema == "identity");
        var createdParam = fn.Parameters.Single(p => p.Name == "CreatedUserId");
        var createdUserColumn = fn.Columns.Single(c => c.Name == "created.user.userId");

        // Assert
        Assert.False(createdParam.IsNullable.GetValueOrDefault(true));
        Assert.False(createdUserColumn.IsNullable.GetValueOrDefault(true));
    }

    private sealed class FakeDbContext : DbContext
    {
        public FakeDbContext() : base(new NullConsoleService())
        {
            SetConnectionString("Server=(local);Database=mock;Trusted_Connection=True;");
        }

        protected override Task<List<T>?> OnListAsync<T>(string queryString, List<SqlParameter> parameters, CancellationToken cancellationToken, AppSqlTransaction? transaction)
        {
            if (typeof(T) == typeof(FunctionRow))
            {
                var row = new FunctionRow
                {
                    schema_name = "identity",
                    function_name = "RecordAsJson",
                    type_code = "FN", // scalar
                    object_id = 1001,
                    definition = @"CREATE FUNCTION [identity].[RecordAsJson]
(
    @Context [core].[Context] READONLY,
    @RecordId int,
    @RowVersion bigint,
    @CreatedUserId int,
    @CreatedDt datetime2,
    @UpdatedUserId int,
    @UpdatedDt datetime2
)
RETURNS NVARCHAR(MAX)
AS
BEGIN
    RETURN (
        SELECT @RecordId AS 'recordId',
               @RowVersion AS 'rowVersion',
               @CreatedDt AS 'created.dateTime',
               @CreatedUserId AS 'created.user.userId',
               uc.UserName AS 'created.user.displayName',
               uc.Initials AS 'created.user.initials',
               @UpdatedDt AS 'updated.dateTime',
               @UpdatedUserId AS 'updated.user.userId',
               uu.UserName AS 'updated.user.displayName',
               uu.Initials AS 'updated.user.initials'
        FROM [identity].[User] AS uc
        LEFT JOIN [identity].[User] AS uu ON uu.UserId = @UpdatedUserId
        WHERE uc.UserId = @CreatedUserId
        FOR JSON PATH, WITHOUT_ARRAY_WRAPPER
    );
END",
                };
                return Task.FromResult<List<T>?>(new List<T> { (T)(object)row });
            }
            if (typeof(T) == typeof(FunctionParamRow))
            {
                var list = new List<FunctionParamRow>();
                int objectId = 1001;
                int ordinal = 1;
                list.Add(MakeParam(objectId, ordinal++, "@RecordId", "int", 4));
                list.Add(MakeParam(objectId, ordinal++, "@RowVersion", "bigint", 8));
                list.Add(MakeParam(objectId, ordinal++, "@CreatedUserId", "int", 4, userTypeName: "_id", userTypeSchema: "core", userTypeIsNullable: 0));
                list.Add(MakeParam(objectId, ordinal++, "@CreatedDt", "datetime2", 8));
                list.Add(MakeParam(objectId, ordinal++, "@UpdatedUserId", "int", 4));
                list.Add(MakeParam(objectId, ordinal++, "@UpdatedDt", "datetime2", 8));
                var casted = list.Select(item => (T)(object)item).ToList();
                return Task.FromResult<List<T>?>(casted);
            }
            if (typeof(T) == typeof(FunctionColumnRow))
            {
                return Task.FromResult<List<T>?>(new List<T>());
            }
            if (typeof(T) == typeof(FunctionDependencyRow))
            {
                return Task.FromResult<List<T>?>(new List<T>());
            }
            return Task.FromResult<List<T>?>(null);
        }

    private static FunctionParamRow MakeParam(int objectId, int ordinal, string name, string sqlType, int length, string userTypeName = null, string userTypeSchema = null, int? userTypeIsNullable = null)
        {
            return new FunctionParamRow
            {
                object_id = objectId,
                ordinal = ordinal,
                param_name = name,
                system_type_name = sqlType,
                normalized_length = length,
                is_output = 0,
                is_nullable = 1,
                has_default_value = 0,
                max_length = length,
                precision = 0,
                scale = 0,
                user_type_name = userTypeName,
                user_type_schema_name = userTypeSchema,
                user_type_is_nullable = userTypeIsNullable,
                base_type_name = sqlType
            };
        }
    }

    private sealed class NullConsoleService : IConsoleService
    {
        public void Info(string message) { }
        public void Error(string message) { }
        public void Warn(string message) { }
        public void Output(string message) { }
        public void Verbose(string message) { }
        public void Success(string message) { }
        public void DrawProgressBar(int percentage, int barSize = 40) { }
        public void Green(string message) { }
        public void Yellow(string message) { }
        public void Red(string message) { }
        public void Gray(string message) { }
        public SpocR.Services.Choice GetSelection(string prompt, List<string> options) => new(0, options.First());
        public SpocR.Services.Choice GetSelectionMultiline(string prompt, List<string> options) => new(0, options.First());
        public bool GetYesNo(string prompt, bool isDefaultConfirmed, ConsoleColor? promptColor = null, ConsoleColor? promptBgColor = null) => isDefaultConfirmed;
        public string GetString(string prompt, string defaultValue = "", ConsoleColor? promptColor = null) => defaultValue;
        public void PrintTitle(string title) { }
        public void PrintImportantTitle(string title) { }
        public void PrintSubTitle(string title) { }
        public void PrintSummary(IEnumerable<string> summary, string headline = null) { }
        public void PrintTotal(string total) { }
        public void PrintDryRunMessage(string message = null) { }
        public void PrintConfiguration(SpocR.Models.ConfigurationModel config) { }
        public void PrintFileActionMessage(string fileName, SpocR.Enums.FileActionEnum fileAction) { }
        public void PrintCorruptConfigMessage(string message) { }
        public void StartProgress(string message) { }
        public void CompleteProgress(bool success = true, string message = null) { }
        public void UpdateProgressStatus(string status, bool success = true, int? percentage = null) { }
    }
}
