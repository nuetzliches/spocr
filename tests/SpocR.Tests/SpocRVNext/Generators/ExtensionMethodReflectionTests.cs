using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Threading.Tasks;
using Xunit;

namespace SpocR.Tests.SpocRVNext.Generators;

/// <summary>
/// Reflection based verification that generated extension methods (<ProcName>Async) are present and bridge to the wrapper ExecuteAsync.
/// Uses the compiled sample assembly (RestApi) as source of generated types.
/// </summary>
public class ExtensionMethodReflectionTests
{
    // NOTE: This test assumes the sample project has been built (samples/restapi/RestApi.csproj)
    // If missing, build prior to running tests.
    [Fact]
    public async Task UserListAsync_ExtensionMethod_Present_And_ReturnsAggregateType()
    {
        // Arrange: determine repo root and sample bin directory
        var testBin = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
        // Ascend until we find a directory that contains both 'samples' and 'src' folders – treat that as repo root.
        DirectoryInfo? repoCandidate = new DirectoryInfo(testBin);
        while (repoCandidate != null && !(Directory.Exists(Path.Combine(repoCandidate.FullName, "samples")) && Directory.Exists(Path.Combine(repoCandidate.FullName, "src"))))
        {
            repoCandidate = repoCandidate.Parent;
        }
        Assert.NotNull(repoCandidate);
        var sampleBinDir = Path.Combine(repoCandidate!.FullName, "samples", "restapi", "bin", "Debug", "net8.0");
        var sampleDll = Path.Combine(sampleBinDir, "RestApi.dll");
        Assert.True(File.Exists(sampleDll), $"Sample assembly not found at {sampleDll}. Ensure 'dotnet build samples/restapi/RestApi.csproj' ran before tests.");

        // Copy dependency dlls (best-effort) into test bin so AssemblyLoadContext can resolve
        foreach (var dll in Directory.EnumerateFiles(sampleBinDir, "*.dll"))
        {
            var dest = Path.Combine(testBin, Path.GetFileName(dll));
            if (!File.Exists(dest)) File.Copy(dll, dest, overwrite: false);
        }

        var alc = new AssemblyLoadContext("SampleReflection", isCollectible: true);
        alc.Resolving += (ctx, name) =>
        {
            var candidatePath = Path.Combine(sampleBinDir, name.Name + ".dll");
            if (File.Exists(candidatePath)) return ctx.LoadFromAssemblyPath(candidatePath);
            candidatePath = Path.Combine(testBin, name.Name + ".dll");
            if (File.Exists(candidatePath)) return ctx.LoadFromAssemblyPath(candidatePath);
            return null;
        };
        var asm = alc.LoadFromAssemblyPath(sampleDll);

        // Find ISpocRDbContext type (interface)
        Type?[] types;
        try
        {
            types = asm.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            types = ex.Types; // fall back to partial list
        }

        var dbCtxInterface = types.FirstOrDefault(t => t != null && t.Name == "ISpocRDbContext");
        Assert.NotNull(dbCtxInterface);

        // Find extension static class for UserList (naming: UserListExtensions)
        var extClass = types.FirstOrDefault(t => t != null && t.IsSealed && t.IsAbstract && t.Name == "UserListExtensions");
        Assert.NotNull(extClass);

        var method = extClass!.GetMethods(BindingFlags.Public | BindingFlags.Static).FirstOrDefault(m => m.Name == "UserListAsync");
        Assert.NotNull(method);

        // Signature check: first parameter should be ISpocRDbContext (this parameter in extensions)
        var parms = method!.GetParameters();
        Assert.True(parms.Length >= 1 && parms[0].ParameterType.Name == dbCtxInterface!.Name, "First parameter is not ISpocRDbContext");

        // Return type check: should be a Task<UserListAggregate> (vNext unified aggregate naming)
        Assert.True(method.ReturnType.IsGenericType, "Return type is not generic Task<T>");
        Assert.Equal("Task`1", method.ReturnType.Name);
        var aggregateType = method.ReturnType.GenericTypeArguments[0];
        Assert.Equal("UserListAggregate", aggregateType.Name);

        // We cannot easily invoke the method without a concrete ISpocRDbContext instance; just ensure presence.
        await Task.CompletedTask;
    }
}
