using Sbvz.Api.Configuration;
using Xunit;

namespace Sbvz.Api.Tests;

public sealed class LocalEnvironmentLoaderTests
{
    [Fact]
    public void FindsEnvironmentFileOnlyAtSolutionRoot()
    {
        var root = Directory.CreateTempSubdirectory("sbvz-config-");

        try
        {
            var child = Directory.CreateDirectory(Path.Combine(root.FullName, "src", "Sbvz.Api"));
            var solutionFile = Path.Combine(root.FullName, "SBVZ.sln");
            var environmentFile = Path.Combine(root.FullName, ".env");
            File.WriteAllText(solutionFile, string.Empty);
            File.WriteAllText(environmentFile, "FICTIONAL=value");

            var result = LocalEnvironmentLoader.FindEnvironmentFile(child.FullName);

            Assert.Equal(environmentFile, result);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public void DoesNotUseUnrelatedParentEnvironmentFile()
    {
        var root = Directory.CreateTempSubdirectory("sbvz-config-");

        try
        {
            var child = Directory.CreateDirectory(Path.Combine(root.FullName, "project"));
            File.WriteAllText(Path.Combine(root.FullName, ".env"), "FICTIONAL=value");

            var result = LocalEnvironmentLoader.FindEnvironmentFile(child.FullName);

            Assert.Null(result);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }
}
