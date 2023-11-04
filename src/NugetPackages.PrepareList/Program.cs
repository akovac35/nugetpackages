// See https://aka.ms/new-console-template for more information
using System.CommandLine;
using NugetPackages.Infrastructure;
using NugetPackages.Model;
using Spectre.Console;
using static NuGet.Protocol.Core.Types.PackageSearchMetadataBuilder;

List<string> searchTerms = new() {
    // packages from specific owners
  "owner:dotnetfoundation",
  "owner:domaindrivendev id:Swashbuckle.AspNetCore",
  "owner:aspnet",
  "owner:oracle",
  "owner:serilog",
  "owner:nloglogging",
  "owner:azure-sdk",
  "owner:fluentassertions",
  "owner:jskinner id:fluentvalidation",
  "owner:jbogard id:mediatr id:automapper",
  "owner:dapper",
  "owner:mudblazor",
  "owner:orleans",
  "owner:confluent",
  "owner:team-rabbitmq",
  "owner:azuread",
  "owner:identity",
  "owner:dotnetframework",
  "owner:polly",
  "owner:RoslynTeam",
  "owner:fody",
  "owner:OpenTelemetry",
  "owner:odata",
  "owner:protobuf-packages",
  "owner:castleproject",
  "owner:AppInsightsSdk",
  "owner:grpc-packages",
  "owner:vstest",
  "owner:rsuter id:njson id:nswag id:nconsole",
  "owner:stephencleary id:nito",
  "owner:SQLitePCLRaw",
  "owner:selenium",
  "owner:Quartz.NET",
  "owner:OpenTracing",
  // individual packages
  "packageid:Consul",
  "packageid:FastExpressionCompiler",
  "packageid:libLLVM",
  "packageid:SharpZipLib",
  "packageid:Spectre.Console",
  "packageid:Spectre.Console.Cli",
  "packageid:Spectre.Console.Json",
  "packageid:Terminal.Gui",
  "packageid:Wcwidth",
  "packageid:NStack.Core",
  "packageid:Stateless",
  "packageid:Portable.BouncyCastle",
  "packageid:BouncyCastle.Cryptography",
  "packageid:NUnit",
  "packageid:NUnit3TestAdapter",
  "packageid:Moq",
  "packageid:Bogus",
  "packageid:CompareNETObjects",
  "packageid:DeepEqual",
  "packageid:DeepCloner",
  "packageid:HtmlAgilityPack",
  "packageid:JetBrains.Annotations",
  "packageid:RestSharp",
  "packageid:NSubstitute",
  "packageid:FakeItEasy",
  "packageid:Fluid.Core",
  "packageid:Parlot",
  "packageid:Namotion.Reflection",
  "packageid:Jint",
};

var rootCommand = new RootCommand("Prepares a list of NuGet packages and stores them in a file in the current directory.");

rootCommand.SetHandler(async (context) =>
{

    List<ClonedPackageSearchMetadata> packageList = new();
    List<Package> packages = new();

    AnsiConsole.MarkupLine("[orange1]Preparing NuGet package list[/]");

    ProgressBarColumn progressBarColumn = new()
    {
        CompletedStyle = new Style(foreground: Color.Orange1)
    };

    SpinnerColumn spinnerColumn = new()
    {
        Style = new Style(foreground: Color.Orange1)
    };

    RemainingTimeColumn remainingTimeColumn = new()
    {
        Style = new Style(foreground: Color.Orange1)
    };

    await AnsiConsole.Progress()
        .Columns(new ProgressColumn[]
        {
            new TaskDescriptionColumn(),
            progressBarColumn,
            new PercentageColumn(),
            remainingTimeColumn,
            spinnerColumn,
        })
        .AutoClear(true)
        .HideCompleted(true)
        .StartAsync(async context =>
        {
            var packageListTask = context.AddTask("Fetching list");
            packageListTask.MaxValue = searchTerms.Count;
            for (var i = 0; i <= packageListTask.MaxValue; i++)
            {
                packageListTask.IsIndeterminate = i == 0;

                if (i != 0)
                {
                    var searchTerm = searchTerms[i - 1];
                    packageList.AddRange(await PackageHelper.SearchPackages(searchTerm: searchTerm));
                    AnsiConsole.MarkupLine($"Fetched package list for search term: [orange1]{searchTerm}[/]");

                    packageListTask.Value = i;
                }
                else
                {
                    packageListTask.Value = i;
                }
            }

            packageList = packageList.DistinctBy(item => item.Identity.Id).ToList();

            var packageMetadataTask = context.AddTask("Fetching metadata");
            packageMetadataTask.MaxValue = packageList.Count;

            await foreach (var item in PackageHelper.ProcessSearchPackagesResult(packageList))
            {
                packageMetadataTask.IsIndeterminate = item.Index == null;
                packageMetadataTask.Value = (item.Index + 1) ?? 0;
                packages.AddRange(item.PackageVersions);

                if (item.Exception != null)
                {
                    AnsiConsole.MarkupLine($"[red]Error fetching metadata for {item.PackageId}: {item.Exception.Message}[/]");
                }
            }

            AnsiConsole.MarkupLine($"Fetched package metadata for [orange1]{packageList.Count}[/] packages");
        });

    /*
    var listoOfPackages = new HashSet<string>();
    var listOfDependencies = new HashSet<string>();

    await AnsiConsole.Status()
        .StartAsync("Processing dependencies ...", async ctx =>
        {
            foreach (var item in packages)
            {
                _ = listoOfPackages.Add(item.Id);
            }

            foreach (var item in packages)
            {
                foreach (var dependency in item.Dependencies)
                {
                    if (!listoOfPackages.TryGetValue(dependency, out var _))
                    {
                        _ = listOfDependencies.Add(dependency);
                    }
                }
            }

            await File.WriteAllTextAsync(path: Path.Combine(Environment.CurrentDirectory, "deps.txt"), contents: System.Text.Json.JsonSerializer.Serialize(listOfDependencies));
        });
    */

    await AnsiConsole.Status()
        .StartAsync("Storing results ...", async ctx =>
        {
            var filePath = Path.Combine(Environment.CurrentDirectory, "package_list.tsv");
            packages = packages.OrderBy(item => item.Id).ToList();

            await File.WriteAllTextAsync(path: filePath, contents: PackageHelper.ProcessPackagesAsString(packages).ToString());

            AnsiConsole.MarkupLine($"[orange1]Stored a list with {packages.Count} entries to:[/]");
            AnsiConsole.Write(new TextPath(filePath).LeafColor(Color.Orange1));
            AnsiConsole.MarkupLine("[green]Done[/]");
        });
});

return await rootCommand.InvokeAsync(args);
