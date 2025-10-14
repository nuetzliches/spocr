using System;
using System.IO;
using SpocR.SpocRVNext.Engine;
using Xunit;
using Shouldly;

namespace SpocR.Tests.SpocRVNext.Templating;

public class SimpleTemplateEngineTests
{
    private readonly SimpleTemplateEngine _engine = new();

    [Fact]
    public void Render_Should_Substitute_TopLevel_Properties()
    {
        var tpl = "Hello {{ Name }}!";
        var result = _engine.Render(tpl, new { Name = "SpocR" });
        result.ShouldBe("Hello SpocR!");
    }

    [Fact]
    public void Render_Should_Substitute_Nested_Properties()
    {
        var tpl = "{{ User.First }} {{ User.Last }}";
        var result = _engine.Render(tpl, new { User = new { First = "Ada", Last = "Lovelace" } });
        result.ShouldBe("Ada Lovelace");
    }

    [Fact]
    public void Render_Missing_Placeholder_Yields_Empty()
    {
        var tpl = "Hello {{ Missing }}";
        var result = _engine.Render(tpl, new { Name = "X" });
        result.ShouldBe("Hello ");
    }

    [Fact]
    public void Render_Null_Model_Returns_Template()
    {
        var tpl = "Hello {{ Name }}";
        var result = _engine.Render(tpl, null);
        result.ShouldBe(tpl); // no substitution without model
    }

    [Fact]
    public void Render_Empty_Template_Returns_Empty()
    {
        var result = _engine.Render(string.Empty, new { A = 1 });
        result.ShouldBe(string.Empty);
    }

}
