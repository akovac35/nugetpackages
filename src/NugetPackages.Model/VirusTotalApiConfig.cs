namespace NugetPackages.Model
{
    public class VirusTotalApiConfig
    {
        public int RequestsPerDay { get; set; }
        public int RequestsPerMinute { get; set; }

        public int PauseInMinutesForHttpStatusCode429 { get; set; }

        public static VirusTotalApiConfig Default => new VirusTotalApiConfig { RequestsPerMinute = 4, RequestsPerDay = 500, PauseInMinutesForHttpStatusCode429 = 10 };
    }
}
