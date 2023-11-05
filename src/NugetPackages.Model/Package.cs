namespace NugetPackages.Model
{
    public class Package
    {
        public string Id { get; init; } = null!;

        public string Version { get; init; } = null!;

        public string? License { get; init; } = null!;

        public string IdWithVersion
        {
            get 
            {
                return $"{Id}.{Version}";
            }
        }

        public IList<string> Dependencies { get; init; } = new List<string>();
    }
}
