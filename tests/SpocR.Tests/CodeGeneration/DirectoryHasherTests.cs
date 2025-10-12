using System.IO;
using Xunit;
using SpocR.SpocRVNext.Utils;

namespace SpocR.Tests.CodeGeneration;

public class DirectoryHasherTests
{
    [Fact]
    public void HashDirectory_Twice_YieldsSameAggregate()
    {
        var temp = Directory.CreateTempSubdirectory();
        File.WriteAllText(Path.Combine(temp.FullName, "A.txt"), "Hello World");
        File.WriteAllText(Path.Combine(temp.FullName, "B.txt"), "Another File\nLine2");

        var first = DirectoryHasher.HashDirectory(temp.FullName);
        var second = DirectoryHasher.HashDirectory(temp.FullName);

        Assert.Equal(first.AggregateSha256, second.AggregateSha256);
        Assert.Equal(first.Files.Count, second.Files.Count);
    }
}
