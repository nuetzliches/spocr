using System.Linq;
using SpocR.Models;
using Xunit;

namespace SpocR.Tests.Cli;

public class JsonParserDoubleSemicolonTests
{
    [Fact]
    public void ForJson_WithWithoutArrayWrapper_AndDoubleSemicolon_ParsesAsSingleJsonResultSet()
    {
        var sql = @"CREATE PROCEDURE [journal].[Template_WordFileFindAsJson]
\t@Context [core].[Context] READONLY
AS
BEGIN

\tDECLARE @RecordId [core].[_id] = [core].[ContextRecordId](@Context);

\tSELECT wf.WordFileId AS 'wordFileId',
\t\tJSON_QUERY((SELECT [identity].[RecordAsJson](@Context, wf.WordFileId, wf.[RowVersion], wf.CreatedById, wf.CreatedDt, wf.UpdatedById, wf.UpdatedDt))) AS 'record',
\t\twf.[FileName] AS 'fileName',
\t\tc.ContextId AS 'context.contextId',
\t\tc.Code AS 'context.code',
\t\tc.DisplayName AS 'context.displayName',
\t\tc.RelativeDotxPath AS 'context.relativeDotxPath',
\t\tct.CommunicationTypeId AS 'communicationType.communicationTypeId',
\t\tct.Code AS 'communicationType.code',
\t\tct.DisplayName AS 'communicationType.displayName',
\t\tct.RelativeDotxPath AS 'communicationType.relativeDotxPath',
\t\t(
\t\t\tSELECT wfp.WordFilePlaceholderId AS 'wordFilePlaceholderId',
\t\t\t\twfp.Code AS 'code',
\t\t\t\twfp.IsValid AS 'isValid',
\t\t\t\tp.PlaceholderId AS 'placeholder.placeholderId',
\t\t\t\tp.DisplayName AS 'placeholder.displayName',
\t\t\t\ttp.IsRequired AS 'placeholder.isRequired'
\t\t\tFROM [journal].[Template_WordFilePlaceholder] AS wfp
\t\t\t\tLEFT OUTER JOIN [journal].[Placeholder] AS p
\t\t\t\t\tON p.PlaceholderId = wfp.PlaceholderId
\t\t\t\tLEFT OUTER JOIN [journal].[Template] AS t
\t\t\t\t\tON t.IsDeleted = 0 
\t\t\t\t\tAND t.ContextId = wf.ContextId
\t\t\t\t\tAND t.WordFileId = wf.WordFileId
\t\t\t\tLEFT OUTER JOIN [journal].[TemplatePlaceholder] AS tp
\t\t\t\t\tON tp.TemplateId = t.TemplateId
\t\t\t\t\tAND tp.PlaceholderId = wfp.PlaceholderId
\t\t\t\t\tAND tp.IsDeleted = 0
\t\t\tWHERE wfp.WordFileId = wf.WordFileId
\t\t\t\tAND wfp.IsDeleted = 0
\t\t\tFOR JSON PATH
\t\t) AS 'placeholders',
\t\tCAST(IIF(EXISTS(SELECT TOP 1 1 
\t\tFROM [journal].[Template_WordFilePlaceholder] AS wp1 
\t\tWHERE wp1.WordFileId = wf.WordFileId
\t\t\tAND wp1.IsDeleted = 0
\t\t\tAND wp1.IsValid = 0), 1, 0) AS BIT) AS 'invalidPlaceholderExists',
\t\tCAST(IIF(EXISTS(SELECT TOP 1 1 
\t\tFROM [journal].[Template] AS t 
\t\tWHERE t.IsDeleted = 0 
\t\t\tAND t.ContextId = wf.ContextId
\t\t\tAND t.WordFileId = wf.WordFileId), 1, 0) AS BIT) AS 'isAssigned'
\tFROM [journal].[Template_WordFile] AS wf
\t\tINNER JOIN [journal].[Context] AS c
\t\t\tON c.ContextId = wf.ContextId
\t\tINNER JOIN [journal].[CommunicationType] AS ct
\t\t\tON ct.CommunicationTypeId = wf.CommunicationTypeId
\tWHERE wf.WordFileId = @RecordId
\t\tAND wf.IsDeleted = 0
\tFOR JSON PATH, WITHOUT_ARRAY_WRAPPER;;

END";

        var content = StoredProcedureContentModel.Parse(sql, "journal");
        Assert.NotNull(content);
        Assert.Single(content.ResultSets);
        var rs = content.ResultSets[0];
        Assert.True(rs.ReturnsJson);
        Assert.False(rs.ReturnsJsonArray); // WITHOUT_ARRAY_WRAPPER -> Objekt
        // Mindestens einige erwartete Spalten
        Assert.Contains(rs.Columns, c => c.Name == "wordFileId");
        Assert.Contains(rs.Columns, c => c.Name == "record");
        Assert.Contains(rs.Columns, c => c.Name == "fileName");
        // Nested JSON Column 'placeholders'
        var placeholders = rs.Columns.FirstOrDefault(c => c.Name == "placeholders");
        Assert.NotNull(placeholders);
        Assert.True(placeholders!.IsNestedJson.HasValue && placeholders.IsNestedJson.Value);
        Assert.True(placeholders.ReturnsJson.HasValue && placeholders.ReturnsJson.Value);
        // Nested columns sollten existieren
        Assert.True(placeholders.Columns.Count > 0);
    }
}
