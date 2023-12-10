using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using NugetPackages.Infrastructure;
using Xunit.Abstractions;

namespace NugetPackages.Test
{
    public class VirusTotalApiTests
    {
        private readonly ITestOutputHelper output;
        private readonly string virusTotalApiKey;

        public VirusTotalApiTests(ITestOutputHelper output)
        {
            this.output = output;
            virusTotalApiKey = Environment.GetEnvironmentVariable("VIRUS_TOTAL_API_KEY") ?? throw new InvalidOperationException("Environment variable VIRUS_TOTAL_API_KEY is not set");
        }

        [Fact]
        public async Task UploadFile_Works()
        {
            using var api = new VirusTotalApi(virusTotalApiKey);

            var validContents = "Valid";
            var result = await api.UploadFile(Encoding.ASCII.GetBytes(validContents), "valid_file.txt");

            output.WriteLine(result);
        }

        [Fact]
        public async Task GetFileReport_ForKnownFile_Works()
        {
            using var api = new VirusTotalApi(virusTotalApiKey);

            var validContents = "Valid";
            var validContentsHash = GeneralHelper.ComputeSha256(validContents);

            output.WriteLine(validContentsHash);

            var result = await api.GetFileReport(validContentsHash);

            result.Should().NotBeNull();
            result!.IsOk().Should().BeTrue();
        }

        [Fact]
        public async Task GetFileReport_ForUnknownFile_Works()
        {
            using var api = new VirusTotalApi(virusTotalApiKey);

            var validContents = "fkj49jfjdk304930437848682389283kdsjflksjdflskjflskjflskdfjlskjflsj983u298urjfsijfso8fudfus98f9sdu8f9sdufs98dfus9d8fu9suf89sduf9sduf9s8uf9su8fwjrlji4lj5h3k5hu398fusdus9dufo43j5o345uh9f7";
            var validContentsHash = GeneralHelper.ComputeSha256(validContents);

            output.WriteLine(validContentsHash);

            var result = await api.GetFileReport(validContentsHash);

            result.Should().BeNull();
        }

        [Fact]
        public async Task GetFileReport_ForVirus_Works()
        {
            using var api = new VirusTotalApi(virusTotalApiKey);

            var virusContents = "TEST_X5O!P%@AP[4\\PZX54(P^)7CC)7}$EICAR-STANDARD-ANTIVIRUS-TEST-FILE!$H+H*";
            var virusContentsHash = GeneralHelper.ComputeSha256(virusContents.Replace("TEST_", ""));

            output.WriteLine(virusContentsHash);

            var result = await api.GetFileReport(virusContentsHash);

            result.Should().NotBeNull();
            result!.IsOk().Should().BeFalse();
        }
    }
}
