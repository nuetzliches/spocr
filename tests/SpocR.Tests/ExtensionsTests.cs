using System;
using SpocR.Extensions;
using Shouldly;
using Xunit;

namespace SpocR.Tests;

public class VersionExtensionsTests
{
    [Theory]
    [InlineData("1.0.0", "1.0.0", 0)]
    [InlineData("1.0.1", "1.0.0", 1)]
    [InlineData("1.1.0", "1.0.5", 1)]
    [InlineData("2.0.0", "2.1.0", -1)]
    public void Compare_Should_Work_On_First_3_Parts(string left, string right, int expectedSign)
    {
        var v1 = Version.Parse(left);
        var v2 = Version.Parse(right);
        var cmp = v1.Compare(v2);
        Math.Sign(cmp).ShouldBe(expectedSign);
    }

    [Fact]
    public void IsGreaterThan_Should_Return_True_When_Left_Is_Newer()
    {
        new Version(1, 2, 3).IsGreaterThan(new Version(1, 2, 2)).ShouldBeTrue();
    }

    [Fact]
    public void IsLessThan_Should_Return_True_When_Left_Is_Older()
    {
        new Version(1, 2, 2).IsLessThan(new Version(1, 2, 3)).ShouldBeTrue();
    }

    [Fact]
    public void Equals_Should_Ignore_Revision_Component()
    {
        // Compare truncates to first 3 components
        var a = new Version(1, 2, 3, 9);
        var b = new Version(1, 2, 3, 0);
        // System.Version.Equals (instance) compares all 4 components => False
        a.Equals(b).ShouldBeFalse("System.Version considers revision component");
        // Extension-based comparison (first 3 parts) => True
        SpocR.Extensions.VersionExtensions.Equals(a, b).ShouldBeTrue("extension Compare truncates to 3 parts");
    }
}

public class StringExtensionsTests
{
    [Theory]
    [InlineData(null, null)]
    [InlineData("", "")]
    [InlineData("Hello", "hello")]
    public void FirstCharToLower_Works(string? input, string? expected)
    {
        input?.FirstCharToLower().ShouldBe(expected);
        if (input == null) return; // null safe already tested
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", "")]
    [InlineData("hello", "Hello")]
    public void FirstCharToUpper_Works(string? input, string? expected)
    {
        input?.FirstCharToUpper().ShouldBe(expected);
    }

    [Theory]
    [InlineData("some_value", "Some_value")]
    [InlineData("some value", "SomeValue")]
    [InlineData("some-value", "SomeValue")]
    [InlineData("some.value", "SomeValue")]
    [InlineData("123abc", "_123abc")]
    [InlineData("__alreadyPascal", "__alreadyPascal")]
    [InlineData("spocr generate command", "SpocrGenerateCommand")]
    public void ToPascalCase_Works(string input, string expected)
    {
        input.ToPascalCase().ShouldBe(expected);
    }
}
