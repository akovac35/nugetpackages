namespace NugetPackages.Model
{
    public class Package
    {
        public string Id { get; init; } = null!;

        public string Version { get; init; } = null!;

        public string? License { get; init; } = null!;

        public IList<string> Dependencies { get; init; } = new List<string>();
    }
}
