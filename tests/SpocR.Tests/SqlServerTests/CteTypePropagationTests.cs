using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using SpocR.Models;
using SpocR.TestFramework;
using Xunit;
using Xunit.Abstractions;

namespace SpocR.Tests.SqlServerTests;

public class CteTypePropagationTests : SqlServerTestBase
{
    public CteTypePropagationTests(ITestOutputHelper output) : base(output)
    {
    }

    [Fact]
    public async Task CteWithForJsonPathSubquery_ShouldPropagateTypesFromCte()
    {
        // Arrange - SQL with CTE and FOR JSON PATH subquery
        var sql = @"
CREATE PROCEDURE [test].[CteTypePropagationTest]
AS
BEGIN
    WITH WTestData AS (
        SELECT 
            CAST(1 AS INT) AS TestId,
            CAST('TestValue' AS NVARCHAR(100)) AS TestValue,
            CAST('TestDisplay' AS NVARCHAR(50)) AS TestDisplay,
            CAST(1 AS BIT) AS IsActive
    )
    SELECT 
        'main' AS 'mainField',
        (   
            SELECT t.TestId AS 'testId',
                t.TestValue AS 'value',
                t.TestDisplay AS 'displayName',
                t.IsActive AS 'isActive'
            FROM WTestData AS t
            FOR JSON PATH
        ) AS 'testData'
    FOR JSON PATH
END";

        // Act
        var result = await ExecuteSqlAndAnalyzeAsync(sql);

        // Assert
        Assert.Single(result.Procedures);
        var procedure = result.Procedures.First();

        Assert.Equal("test", procedure.Schema);
        Assert.Equal("CteTypePropagationTest", procedure.Name);

        // Should have exactly 1 ResultSet (CTE should not generate separate ResultSet)
        Assert.Single(procedure.ResultSets);
        var resultSet = procedure.ResultSets.First();

        // Should have 2 columns: mainField and testData
        Assert.Equal(2, resultSet.Columns.Count);

        // Check mainField
        var mainField = resultSet.Columns.First(c => c.Name == "mainField");
        Assert.Null(mainField.SqlTypeName); // literal projections remain typeless; snapshot layer resolves later

        // Check testData (FOR JSON PATH subquery)
        var testData = resultSet.Columns.First(c => c.Name == "testData");
        Assert.True(testData.ReturnsJson);
        Assert.True(testData.ReturnsJsonArray);

        // Critical: testData should have properly typed columns from CTE
        Assert.Equal(4, testData.Columns.Count);

        var testId = testData.Columns.First(c => c.Name == "testId");
        Assert.Equal("int", testId.SqlTypeName);
        Assert.Null(testId.MaxLength); // fixed-size types no longer carry redundant length metadata

        var value = testData.Columns.First(c => c.Name == "value");
        Assert.Equal("nvarchar", value.SqlTypeName);
        Assert.Null(value.MaxLength); // nvarchar(max) inference handled via TypeRef at snapshot level

        var displayName = testData.Columns.First(c => c.Name == "displayName");
        Assert.Equal("nvarchar", displayName.SqlTypeName);
        Assert.Null(displayName.MaxLength);

        var isActive = testData.Columns.First(c => c.Name == "isActive");
        Assert.Equal("bit", isActive.SqlTypeName);
        Assert.Equal(1, isActive.MaxLength);
    }

    [Fact]
    public async Task RoleClaimListAsJson_ShouldHaveCorrectCteTypePropagation()
    {
        // Arrange - The actual procedure from the issue
        var sql = @"
CREATE PROCEDURE [identity].[RoleClaimListAsJson]
	@UserId INT,
	@RoleId INT
AS
BEGIN
	WITH WRoleClaim AS (
		SELECT 
			c.ClaimId,
			c.ClaimTypeId,
			c.ClaimValue,
			c.DisplayName,
			CAST(IIF(rc.RoleClaimId IS NOT NULL, 1, 0) AS BIT) AS IsChecked
		FROM [identity].Claim AS c
			INNER JOIN [identity].[ClaimType] AS ct
				ON ct.ClaimTypeId = c.ClaimTypeId
			LEFT OUTER JOIN [identity].[RoleClaim] AS rc
				ON rc.ClaimId = c.ClaimId
					AND rc.RoleId = @RoleId
	)

	SELECT ct.ClaimTypeId AS 'claimTypeId',
		ct.Code AS 'code',
		ct.DisplayName AS 'displayName',
		ct.OrderIx AS 'oderIx',
		(	
			SELECT c.ClaimId AS 'claimId',
				c.ClaimValue AS 'value',
				c.DisplayName AS 'displayName',
				c.IsChecked AS 'isChecked'
			FROM WRoleClaim AS c
			WHERE c.ClaimTypeId = ct.ClaimTypeId
			ORDER BY c.DisplayName ASC
			FOR JSON PATH
		) AS 'claims'
	FROM [identity].[ClaimType] AS ct 
	WHERE EXISTS(
		SELECT TOP 1 1 
		FROM WRoleClaim AS c1
		WHERE c1.ClaimTypeId = ct.ClaimTypeId
	)
	ORDER BY ct.OrderIx ASC, ct.DisplayName ASC
	FOR JSON PATH
END";

        var columnTypes = new Dictionary<string, (string SqlTypeName, int? MaxLength, bool? IsNullable)>(StringComparer.OrdinalIgnoreCase)
        {
            ["identity.Claim.ClaimId"] = ("int", null, false),
            ["identity.Claim.ClaimTypeId"] = ("int", null, false),
            ["identity.Claim.ClaimValue"] = ("nvarchar", null, true),
            ["identity.Claim.DisplayName"] = ("nvarchar", null, false),
            ["identity.ClaimType.ClaimTypeId"] = ("int", null, false),
            ["identity.ClaimType.Code"] = ("nvarchar", null, false),
            ["identity.ClaimType.DisplayName"] = ("nvarchar", null, false),
            ["identity.ClaimType.OrderIx"] = ("int", null, false),
            ["identity.RoleClaim.RoleClaimId"] = ("int", null, false),
            ["identity.RoleClaim.RoleId"] = ("int", null, false),
            ["identity.RoleClaim.ClaimId"] = ("int", null, false)
        };

        var previousResolver = StoredProcedureContentModel.ResolveTableColumnType;
        StoredProcedureContentModel.ResolveTableColumnType = (schema, table, column) =>
        {
            if (string.IsNullOrWhiteSpace(column)) return (string.Empty, null, null);

            var normalizedSchema = (schema ?? string.Empty).Trim();
            var normalizedTable = (table ?? string.Empty).Trim();
            var normalizedColumn = column.Trim();

            foreach (var key in BuildKeys(normalizedSchema, normalizedTable, normalizedColumn))
            {
                if (columnTypes.TryGetValue(key, out var info))
                {
                    return info;
                }
            }

            return (string.Empty, null, null);
        };

        try
        {
            // Act
            var result = await ExecuteSqlAndAnalyzeAsync(sql);

            // Assert
            Assert.Single(result.Procedures);
            var procedure = result.Procedures.First();

            // Should have exactly 1 ResultSet (CTE should not generate separate ResultSet)
            Assert.Single(procedure.ResultSets);
            var resultSet = procedure.ResultSets.First();

            // Should have 5 columns including claims FOR JSON PATH subquery
            Assert.Equal(5, resultSet.Columns.Count);

            // Check claims column (FOR JSON PATH subquery)
            var claims = resultSet.Columns.First(c => c.Name == "claims");
            Assert.True(claims.ReturnsJson);
            Assert.True(claims.ReturnsJsonArray);

            // Critical: claims should have properly typed columns from CTE
            Assert.Equal(4, claims.Columns.Count);

            var claimId = claims.Columns.First(c => c.Name == "claimId");
            Assert.Equal("int", claimId.SqlTypeName);
            Assert.Null(claimId.MaxLength);

            var value = claims.Columns.First(c => c.Name == "value");
            Assert.Equal("nvarchar", value.SqlTypeName);
            Assert.Null(value.MaxLength);

            var displayName = claims.Columns.First(c => c.Name == "displayName");
            Assert.Equal("nvarchar", displayName.SqlTypeName);
            Assert.Null(displayName.MaxLength);

            var isChecked = claims.Columns.First(c => c.Name == "isChecked");
            Assert.Equal("bit", isChecked.SqlTypeName);
            Assert.Equal(1, isChecked.MaxLength);
        }
        finally
        {
            StoredProcedureContentModel.ResolveTableColumnType = previousResolver;
        }
    }

    private static IEnumerable<string> BuildKeys(string schema, string table, string column)
    {
        if (!string.IsNullOrWhiteSpace(schema) && !string.IsNullOrWhiteSpace(table))
        {
            yield return $"{schema}.{table}.{column}";
        }

        if (!string.IsNullOrWhiteSpace(table))
        {
            yield return $"{table}.{column}";
        }

        yield return column;
    }
}