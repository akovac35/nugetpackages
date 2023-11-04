namespace NugetPackages.Model
{
    public class PackageDownloadResult
    {
        public string FullPath { get; init; } = null!;
        public int? Index { get; set; }

        public int Count { get; init; }

        public Exception? Exception { get; init; }

        public string PackageId { get; init; } = null!;
    }
}
