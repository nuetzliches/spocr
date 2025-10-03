using System.Collections.Generic;
using System.Text.Json;
using FluentAssertions;
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

    [Fact]
    public void Deserialize_List_Model_Should_Work()
    {
        // Raw method analogue
        var raw = GetArrayJson();

        // Generated pattern: System.Text.Json.JsonSerializer.Deserialize<List<UserListAsJson>>(await Raw()) ?? new List<UserListAsJson>()
        var typed = JsonSerializer.Deserialize<List<UserListAsJson>>(raw) ?? new List<UserListAsJson>();

        typed.Should().HaveCount(2);
        typed[0].Id.Should().Be("1");
        typed[1].Name.Should().Be("Bob");
    }

    [Fact]
    public void Deserialize_Single_Model_Should_Work()
    {
        var raw = GetSingleJson();
        var typed = JsonSerializer.Deserialize<UserFindAsJson>(raw);

        typed.Should().NotBeNull();
        typed!.Id.Should().Be("42");
        typed.Name.Should().Be("Zaphod");
    }

    [Fact]
    public void Deserialize_List_Null_Fallback_Should_Return_Empty_List()
    {
        string raw = "null"; // JSON literal null
        var typed = JsonSerializer.Deserialize<List<UserListAsJson>>(raw) ?? new List<UserListAsJson>();
        typed.Should().BeEmpty();
    }
}
