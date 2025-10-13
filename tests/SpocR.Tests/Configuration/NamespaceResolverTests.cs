using System;
using System.IO;
using Xunit;
using SpocRVNext.Configuration;

namespace SpocR.Tests.Configuration;

public class NamespaceResolverTests
{
    [Fact]
    public void ExplicitOverride_Wins()
    {
        var cfg = new EnvConfiguration { NamespaceRoot = "Custom.Namespace" };
        var resolver = new NamespaceResolver(cfg);
        Assert.Equal("Custom.Namespace", resolver.Resolve());
    }

    [Fact]
    public void UpwardCsproj_BaseName()
    {
        var root = Directory.CreateTempSubdirectory();
        File.WriteAllText(Path.Combine(root.FullName, "App.csproj"), "<Project><PropertyGroup><RootNamespace>MyRoot</RootNamespace></PropertyGroup></Project>");
        var sub = Directory.CreateDirectory(Path.Combine(root.FullName, "feature", "api"));
        var cfg = new EnvConfiguration();
        var resolver = new NamespaceResolver(cfg);
        var ns = resolver.Resolve(sub.FullName);
    Assert.Equal("MyRoot.Feature.Api", ns);
    }

    [Fact]
    public void UpwardCsproj_AssemblyNameFallback()
    {
        var root = Directory.CreateTempSubdirectory();
        File.WriteAllText(Path.Combine(root.FullName, "App.csproj"), "<Project><PropertyGroup><AssemblyName>MyAsm</AssemblyName></PropertyGroup></Project>");
        var cfg = new EnvConfiguration();
        var resolver = new NamespaceResolver(cfg);
        var ns = resolver.Resolve(root.FullName);
    Assert.Equal("MyAsm", ns);
    }

    [Fact]
    public void NoCsproj_DirectoryFallback()
    {
        var root = Directory.CreateTempSubdirectory();
        var cfg = new EnvConfiguration();
        var resolver = new NamespaceResolver(cfg);
        var ns = resolver.Resolve(root.FullName);
        Assert.NotNull(ns);
        Assert.DoesNotContain("..", ns);
    }

    [Fact]
    public void OutputDir_DoesNotAffectNamespace()
    {
        var root = Directory.CreateTempSubdirectory();
        File.WriteAllText(Path.Combine(root.FullName, "App.csproj"), "<Project><PropertyGroup><RootNamespace>RootProj</RootNamespace></PropertyGroup></Project>");
        var cfg = new EnvConfiguration { OutputDir = "SpocR" };
        var resolver = new NamespaceResolver(cfg);
        var ns = resolver.Resolve(root.FullName);
        Assert.Equal("RootProj", ns);
    }
}
