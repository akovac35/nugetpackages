using System.Text.Json;
using FluentAssertions;
using NugetPackages.Infrastructure;
using Xunit.Abstractions;

namespace NugetPackages.Test
{
    public class PackageHelperTests
    {
        private readonly ITestOutputHelper output;

        public PackageHelperTests(ITestOutputHelper output)
        {
            this.output = output;
        }

        [Fact]
        public async Task SearchPackages_Works()
        {
            var packages = await PackageHelper.SearchPackages(searchTerm: "owner:aspnet");

            _ = packages.Should().NotBeEmpty();

            var tmp = packages.First(item => item.Identity.Id == "Microsoft.Extensions.Logging.Abstractions");

            output.WriteLine($"Retrieved {packages.Count} packages");
            output.WriteLine($"Package metadata: {JsonSerializer.Serialize(tmp)}");

            var isDeprecated = await tmp.GetDeprecationMetadataAsync();
            _ = isDeprecated.Should().BeNull();
        }

        [Fact]
        public async Task FilterDeprected_Works()
        {
            var packages = await PackageHelper.SearchPackages(searchTerm: "owner:azure-sdk");

            _ = packages.Should().NotBeEmpty();

            var tmp = packages.First(item => item.Identity.Id == "Microsoft.Rest.ClientRuntime");

            var isDeprecated = await tmp.GetDeprecationMetadataAsync();
            _ = isDeprecated.Should().NotBeNull();
        }

        [Fact]
        public async Task GetPackageMetadatas_Works()
        {
            var metadatas = await PackageHelper.GetPackageMetadatas(packageId: "Microsoft.Extensions.Logging.Abstractions");

            foreach (var metadata in metadatas)
            {
                output.WriteLine($"{metadata.Version} {metadata.LicenseExpression}");
            }
        }

        [Fact]
        public async Task Sandbox()
        {
            var results = (await PackageHelper.SearchPackages("owner:newtonsoft")).ToList();
            results.AddRange(await PackageHelper.SearchPackages("owner:domaindrivendev"));
            results = results.DistinctBy(item => item.Identity.Id).ToList();

            var packages = await PackageHelper.ProcessSearchPackagesResult(results).ToListAsync();

            var table = PackageHelper.ProcessPackagesAsString(packages.SelectMany(item => item.PackageVersions).ToList());

            output.WriteLine(table.ToString());
        }
    }
}