namespace CimianAdmin.Infrastructure.Tests;

internal static class TestPaths
{
    /// <summary>
    /// Walks up from the test binary directory until it finds the repository root
    /// (identified by the presence of <c>CimianAdmin.sln</c>) and returns the path
    /// to <c>samples/SampleRepository</c>.
    /// </summary>
    public static string SampleRepository
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "CimianAdmin.sln")))
            {
                directory = directory.Parent;
            }

            if (directory is null)
            {
                throw new DirectoryNotFoundException("Could not locate the CimianAdmin repository root from the test binary directory.");
            }

            return Path.Combine(directory.FullName, "samples", "SampleRepository");
        }
    }
}
