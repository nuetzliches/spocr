using System.Collections.Generic;
using System.Text.Json;
using Shouldly;
using Xunit;

namespace SpocR.IntegrationTests;

/// <summary>
/// Runtime-level test that mimics the generated JSON deserialize logic without hitting a real database.
/// Ensures consistency with the generator pattern (Raw method returns JSON string, Deserialize method deserializes it).
/// </summary>
public class JsonRuntimeDeserializationTests
{
    private record UserListAsJson(string Id, string Name);
    private record UserFindAsJson(string Id, string Name);

    private static string GetArrayJson() => "[{\"Id\":\"1\",\"Name\":\"Alice\"},{\"Id\":\"2\",\"Name\":\"Bob\"}]";
    private static string GetSingleJson() => "{\"Id\":\"42\",\"Name\":\"Zaphod\"}";
    private static string GetSingleJsonWithoutWrapper() => "{\"Id\":\"7\",\"Name\":\"Trillian\"}"; // simulating ReturnsJsonWithoutArrayWrapper

    [Fact]
    public void Deserialize_List_Model_Should_Work()
    {
        // Raw method analogue
        var raw = GetArrayJson();

        // Generated pattern: System.Text.Json.JsonSerializer.Deserialize<List<UserListAsJson>>(await Raw()) ?? new List<UserListAsJson>()
        var typed = JsonSerializer.Deserialize<List<UserListAsJson>>(raw) ?? new List<UserListAsJson>();

        typed.Count.ShouldBe(2);
        typed[0].Id.ShouldBe("1");
        typed[1].Name.ShouldBe("Bob");
    }

    [Fact]
    public void Deserialize_Single_Model_Should_Work()
    {
        var raw = GetSingleJson();
        var typed = JsonSerializer.Deserialize<UserFindAsJson>(raw);

        typed.ShouldNotBeNull();
        typed!.Id.ShouldBe("42");
        typed.Name.ShouldBe("Zaphod");
    }

    [Fact]
    public void Deserialize_List_Null_Fallback_Should_Return_Empty_List()
    {
        string raw = "null"; // JSON literal null
        var typed = JsonSerializer.Deserialize<List<UserListAsJson>>(raw) ?? new List<UserListAsJson>();
        typed.ShouldBeEmpty();
    }

    [Fact]
    public void Deserialize_List_Empty_Array_Should_Return_Empty_List()
    {
        string raw = "[]";
        var typed = JsonSerializer.Deserialize<List<UserListAsJson>>(raw) ?? new List<UserListAsJson>();
        typed.ShouldBeEmpty();
    }

    [Fact]
    public void Deserialize_Array_With_Whitespace_Should_Work()
    {
        string raw = "  \n  " + GetArrayJson() + "  \n  ";
        var typed = JsonSerializer.Deserialize<List<UserListAsJson>>(raw.Trim()) ?? new List<UserListAsJson>();
        typed.Count.ShouldBe(2);
    }

    [Fact]
    public void Deserialize_Single_NoArrayWrapper_Should_Work()
    {
        var raw = GetSingleJsonWithoutWrapper();
        var typed = JsonSerializer.Deserialize<UserFindAsJson>(raw);
        typed.ShouldNotBeNull();
        typed!.Name.ShouldBe("Trillian");
    }

    [Fact]
    public void Deserialize_Malformed_Json_Should_Throw()
    {
        // Deliberately malformed (trailing comma before closing brace)
        var raw = "{\"Id\":\"1\",\"Name\":\"Broken\",}"; // malformed JSON
        var act = () => JsonSerializer.Deserialize<UserFindAsJson>(raw);
        Should.Throw<JsonException>(act);
    }
}
