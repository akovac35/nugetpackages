namespace NugetPackages.Model
{
    public class PackageProcessingResult
    {
        public IList<Package> PackageVersions { get; init; } = new List<Package>();

        public int? Index { get; set; }

        public int Count { get; init; }

        public Exception? Exception { get; init; }

        public string PackageId { get; init; } = null!;
    }
}
