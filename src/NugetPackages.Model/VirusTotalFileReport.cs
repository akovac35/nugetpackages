using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace NugetPackages.Model
{
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
    public class VirusTotalFileReport
    {
        public Data data { get; set; }

        public bool IsOk()
        {
            // a provider will either flag a file as harmless or undetected, but not both
            if ((data.attributes.last_analysis_stats.undetected + data.attributes.last_analysis_stats.harmless) > 50
                && data.attributes.last_analysis_stats.malicious == 0
                && data.attributes.last_analysis_stats.suspicious == 0)
            {
                return true;
            }
            return false;
        }

        public string ToJsonString()
        {
            return JsonSerializer.Serialize(this);
        }
    }

    public class Data
    {
        public Attributes attributes { get; set; }
        public string type { get; set; }
        public string id { get; set; }
        public Links links { get; set; }
    }

    [SuppressMessage("Naming", "CA1707")]
    [SuppressMessage("Design", "CA1002")]
    [SuppressMessage("Usage", "CA2227")]
    public class Attributes
    {
        public string type_description { get; set; }
        public string vhash { get; set; }
        public List<string> type_tags { get; set; }
        public List<string> names { get; set; }
        public long last_modification_date { get; set; }
        public string type_tag { get; set; }
        public int times_submitted { get; set; }
        public TotalVotes total_votes { get; set; }
        public int size { get; set; }
        public string type_extension { get; set; }
        public long last_submission_date { get; set; }
        public Dictionary<string, AnalysisResult> last_analysis_results { get; set; }
        public string sha256 { get; set; }
        public List<string> tags { get; set; }
        public long last_analysis_date { get; set; }
        public int unique_sources { get; set; }
        public long first_submission_date { get; set; }
        public string ssdeep { get; set; }
        public string md5 { get; set; }
        public string sha1 { get; set; }
        public string magic { get; set; }
        public LastAnalysisStats last_analysis_stats { get; set; }
        public string meaningful_name { get; set; }
        public int reputation { get; set; }
    }

    public class TotalVotes
    {
        public int harmless { get; set; }
        public int malicious { get; set; }
    }

    [SuppressMessage("Naming", "CA1707")]
    public class AnalysisResult
    {
        public string category { get; set; }
        public string engine_name { get; set; }
        public string engine_version { get; set; }
        public object result { get; set; }
        public string method { get; set; }
        public string engine_update { get; set; }
    }


    [SuppressMessage("Naming", "CA1707")]
    public class LastAnalysisStats
    {
        public int harmless { get; set; }
        public int type_unsupported { get; set; }
        public int suspicious { get; set; }
        public int confirmed_timeout { get; set; }
        public int timeout { get; set; }
        public int failure { get; set; }
        public int malicious { get; set; }
        public int undetected { get; set; }
    }

    public class Links
    {
        public string self { get; set; }
    }
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
}
