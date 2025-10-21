using System.Linq;
using Xunit;
using SpocR.Services;

namespace SpocR.Tests.Cli;

public class StatusFindAsJsonAstTests
{
    [Fact]
    public void StatusFindAsJson_FunctionSelect_ForJsonColumnsExtracted()
    {
        var sql = @"CREATE FUNCTION [workflow].[StatusFindAsJson]
(
	@Context			[core].[Context] READONLY,
	@StatusId			[core].[id],
	-- <Recursion>
	@MaxRecursion		[core].[number],
	@CurrentRecursion	[core].[number]
	-- </Recursion>
)
RETURNS NVARCHAR(MAX)
AS
BEGIN

	SET @MaxRecursion = ISNULL(@MaxRecursion, 1) - 1;
	SET @CurrentRecursion = ISNULL(@CurrentRecursion, 0) + 1;

	IF(@MaxRecursion < -1) RETURN NULL;

	DECLARE @Result NVARCHAR(MAX) = JSON_QUERY((
		-- <status>
		SELECT s.StatusId AS 'statusId', 
			s.NodeId AS 'nodeId',
			--s.[RowVersion],
			--s.WorkflowId AS 'workflowId',
			s.IsActive AS 'isActive',
			s.IsHidden AS 'isHidden',
			s.DisplayName AS 'displayName',
			s.[Description] AS 'description',
			-- <paths>
			(SELECT p.PathId AS 'pathId',
					p.[RowVersion] AS 'record.rowVersion',
					p.OrderNo AS 'orderNo',
					p.DirectionCode AS 'directionCode',
					p.DisplayName AS 'displayName',
					-- <status> -- recursion
					JSON_QUERY((SELECT [workflow].[StatusFindAsJson](@Context, p.RemoteStatusId, @MaxRecursion, @CurrentRecursion))) AS 'status',
					-- </status>
					-- <actions>
					(SELECT a.StatusActionId AS 'statusActionId',
							a.[RowVersion] AS 'record.rowVersion',
							a.StatusId AS 'status.statusId',
							a.StatusIsHidden AS 'status.isHidden',
							a.ActionId AS 'action.actionId',
							a.DisplayName AS 'action.displayName',
							a.[Description] AS 'action.description',
							JSON_QUERY(a.[Configuration]) AS 'action.configuration',
							a.ActionIsHidden AS 'action.isHidden',
							a_t.TypeId AS 'action.type.typeId',
							a_t.Code AS 'action.type.code',
							a_t.DisplayName AS 'action.type.displayName',
							a_t.IsUserAction AS 'action.type.isUserAction'
						FROM [workflow].[ActionListByPath](@Context, p.PathId) AS a
							INNER JOIN [workflow].[Action_Type] AS a_t
								ON a_t.TypeId = a.TypeId
						WHERE a.DirectionCode = 'out'
						ORDER BY a.OrderNo ASC
						FOR JSON PATH
					) AS 'outStatusActions'
					-- </actions>
				FROM [workflow].[PathListByStatus](@Context, s.StatusId) AS p
				WHERE @MaxRecursion >= 0
					AND (@CurrentRecursion = 1 OR p.DirectionCode = 'out')
				ORDER BY IIF(p.DirectionCode = 'in', 0, 1) ASC,
					p.OrderNo ASC
				FOR JSON PATH
			) AS 'paths',
			-- </paths>
			-- <actions>
			(SELECT a.StatusActionId AS 'statusActionId',
					a.[RowVersion] AS 'record.rowVersion',
					a.StatusId AS 'status.statusId',
					a.StatusIsHidden AS 'status.isHidden',
					a.ActionId AS 'action.actionId',
					a.DisplayName AS 'action.displayName',
					a.[Description] AS 'action.description',
					JSON_QUERY(a.[Configuration]) AS 'action.configuration',
					a.ActionIsHidden AS 'action.isHidden',
					a_t.TypeId AS 'action.type.typeId',
					a_t.Code AS 'action.type.code',
					a_t.DisplayName AS 'action.type.displayName',
					a_t.IsUserAction AS 'action.type.isUserAction'
				FROM [workflow].[ActionListByStatus](@Context, s.StatusId) AS a
					INNER JOIN [workflow].[Action_Type] AS a_t
						ON a_t.TypeId = a.TypeId
				WHERE a.DirectionCode = 'in'
				ORDER BY a.OrderNo ASC
				FOR JSON PATH
			) AS 'inStatusActions'
			-- </actions>
		FROM [workflow].[StatusFind](@Context, @StatusId) AS s		
		FOR JSON PATH, WITHOUT_ARRAY_WRAPPER
		-- </status>
	));

	RETURN @Result;
END";

        var ast = new JsonFunctionAstExtractor().Parse(sql);
        Assert.True(ast.ReturnsJson, "FOR JSON nicht erkannt");
        Assert.False(ast.ReturnsJsonArray, "WITHOUT_ARRAY_WRAPPER sollte Objekt liefern");
        // Top-Level Spalten
        var names = ast.Columns.Select(c => c.Name).ToList();
        Assert.Contains("statusId", names);
        Assert.Contains("nodeId", names);
        Assert.Contains("isActive", names);
        Assert.Contains("isHidden", names);
        Assert.Contains("displayName", names);
        Assert.Contains("description", names);
        Assert.True(names.Contains("paths"), "paths missing. Extracted columns: " + string.Join(",", names));
        Assert.Contains("inStatusActions", names);

        var pathsCol = ast.Columns.First(c => c.Name == "paths");
        Assert.True(pathsCol.IsNestedJson);
        Assert.True(pathsCol.Children.Count > 0);
        // Rekursive status in paths
        Assert.Contains(pathsCol.Children, ch => ch.Name == "status");
    }
}
