using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;
using SpocR.SpocRVNext.Metadata;
using SpocR.SpocRVNext.Diagnostics;

namespace SpocR.Tests.SpocRVNext.Diagnostics;

public class JsonResultSetAuditTests
{
    [Fact]
    public void Audit_Flags_StringPlaceholders_ForNumeric()
    {
        // Arrange: build descriptor manually (no snapshot dependency)
        var fields = new List<FieldDescriptor>
        {
            new("workflowId","workflowId","string", false, "int", null, null, null),
            new("isActive","isActive","string", false, "bit", null, null, null)
        };
        var rs = new ResultSetDescriptor(0, "ResultSet1", fields, ReturnsJson: true, ReturnsJsonArray: true);
        var proc = new ProcedureDescriptor("WorkflowListAsJson", "dbo", "dbo__WorkflowListAsJson", Array.Empty<FieldDescriptor>(), Array.Empty<FieldDescriptor>(), new List<ResultSetDescriptor> { rs });

        // Act
        var findings = JsonResultSetAudit.Run(new[] { proc });

        // Assert
        Assert.Equal(2, findings.Count);
        Assert.Contains(findings, f => f.Field == "workflowId" && f.Suggested == "int");
        Assert.Contains(findings, f => f.Field == "isActive" && f.Suggested == "bool");
    }
}
