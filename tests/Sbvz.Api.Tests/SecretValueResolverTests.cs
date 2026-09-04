using Sbvz.Api.Configuration;
using Xunit;

namespace Sbvz.Api.Tests;

public sealed class SecretValueResolverTests
{
    [Fact]
    public void DirectValueTakesPrecedenceOverFile()
    {
        var directory = Directory.CreateTempSubdirectory("sbvz-secret-");

        try
        {
            var filePath = Path.Combine(directory.FullName, "secret");
            File.WriteAllText(filePath, "file-value\n");

            var result = SecretValueResolver.Resolve("direct-value", filePath);

            Assert.Equal("direct-value", result);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public void ReadsAndTrimsAbsoluteSecretFile()
    {
        var directory = Directory.CreateTempSubdirectory("sbvz-secret-");

        try
        {
            var filePath = Path.Combine(directory.FullName, "secret");
            File.WriteAllText(filePath, "file-value\n");

            var result = SecretValueResolver.Resolve(null, filePath);

            Assert.Equal("file-value", result);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("relative-secret")]
    public void MissingOrInvalidFileConfigurationReturnsEmpty(string? filePath)
    {
        var result = SecretValueResolver.Resolve(null, filePath);

        Assert.Empty(result);
    }

    [Fact]
    public void RejectsOversizedSecretFile()
    {
        var directory = Directory.CreateTempSubdirectory("sbvz-secret-");

        try
        {
            var filePath = Path.Combine(directory.FullName, "secret");
            File.WriteAllText(filePath, new string('A', 64 * 1024 + 1));

            var result = SecretValueResolver.Resolve(null, filePath);

            Assert.Empty(result);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }
}
