namespace Cloudstrap.Proxy.Tests.Infrastructure
{
    /// <summary>Locates the repository on disk for the file-reading tripwires.</summary>
    internal static class RepositoryPaths
    {
        private static readonly Lazy<string> _root = new(FindRoot);

        /// <summary>Gets the repository root: the directory that contains <c>src/Cloudstrap.sln</c>.</summary>
        public static string Root => _root.Value;

        private static string FindRoot()
        {
            DirectoryInfo? current = new(AppContext.BaseDirectory);
            while (current is not null)
            {
                if (File.Exists(Path.Combine(current.FullName, "src", "Cloudstrap.sln")))
                {
                    return current.FullName;
                }

                current = current.Parent;
            }

            throw new DirectoryNotFoundException(
                $"Could not locate the repository root (a directory containing src/Cloudstrap.sln) above '{AppContext.BaseDirectory}'.");
        }
    }
}
