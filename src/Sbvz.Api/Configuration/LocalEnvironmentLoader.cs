using DotNetEnv;

namespace Sbvz.Api.Configuration;

internal static class LocalEnvironmentLoader
{
    private const string EnvironmentFileName = ".env";
    private const string SolutionFileName = "SBVZ.sln";

    public static void LoadWhenDevelopment()
    {
        var environment = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")
            ?? Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");

        if (!string.Equals(environment, Environments.Development, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var environmentFile = FindEnvironmentFile(Directory.GetCurrentDirectory());

        if (environmentFile is not null)
        {
            Env.NoClobber().Load(environmentFile);
        }
    }

    internal static string? FindEnvironmentFile(string startDirectory)
    {
        var directory = new DirectoryInfo(startDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, SolutionFileName)))
            {
                var environmentFile = Path.Combine(directory.FullName, EnvironmentFileName);

                return File.Exists(environmentFile) ? environmentFile : null;
            }

            directory = directory.Parent;
        }

        return null;
    }
}
