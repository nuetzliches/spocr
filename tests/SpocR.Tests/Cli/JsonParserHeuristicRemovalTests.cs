using System;
using System.Linq;
using FluentAssertions;
using Xunit;
using SpocR.Models;

namespace SpocR.Tests.Cli;

public class JsonParserHeuristicRemovalTests
{
    [Fact]
    public void Procedure_Name_Ending_AsJson_Should_Not_Imply_Json_When_No_ForJson()
    {
        var def = @"CREATE PROCEDURE dbo.GetUsersAsJson AS SELECT Id, Name FROM dbo.Users"; // no FOR JSON
        var content = StoredProcedureContentModel.Parse(def);
        // Parser should not detect any JSON result set
        content.ResultSets.Should().BeNullOrEmpty();
    }
}
