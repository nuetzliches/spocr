using System;
using System.IO;
using Xunit;
using SpocR.SpocRVNext.Configuration;

namespace SpocR.Tests.Configuration;

public class NamespaceResolverTests
{
    [Fact]
    public void Resolve_ReturnsExplicitNamespace()
    {
        var cfg = new EnvConfiguration { NamespaceRoot = "Custom.Namespace" };
        var resolver = new NamespaceResolver(cfg);
        Assert.Equal("Custom.Namespace", resolver.Resolve());
    }

    [Fact]
    public void Resolve_IgnoresCsproj_WhenExplicitNamespaceProvided()
    {
        var root = Directory.CreateTempSubdirectory();
        File.WriteAllText(Path.Combine(root.FullName, "App.csproj"), "<Project><PropertyGroup><RootNamespace>Ignored</RootNamespace></PropertyGroup></Project>");
        var cfg = new EnvConfiguration { NamespaceRoot = "Explicit.NS" };
        var resolver = new NamespaceResolver(cfg);
        var ns = resolver.Resolve(root.FullName);
        Assert.Equal("Explicit.NS", ns);
    }
}
