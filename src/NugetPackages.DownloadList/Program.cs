// See https://aka.ms/new-console-template for more information
using System.CommandLine;
using System.IO.Compression;
using NugetPackages.Infrastructure;
using NugetPackages.Model;
using Spectre.Console;
using Color = Spectre.Console.Color;

var fileOption = new Option<FileInfo>(
                new[] { "--file", "-f" },
                "Path to the file containing a list of NuGet packages to download."
            )
{
    IsRequired = true
};

var destinationOption = new Option<DirectoryInfo>(
                new[] { "--destination", "-d" },
                "Path where NuGet packages will be stored."
            )
{
    IsRequired = false
};

var unpackPackages = new Option<bool>(
            new[] { "--unpack", "-u" },
            "Unpacks downloaded NuGet packages.")
{
    IsRequired = false
};

var rootCommand = new RootCommand("Downloads NuGet packages.")
            {
    fileOption,
    destinationOption,
    unpackPackages
};

rootCommand.SetHandler(async (context) =>
{
    var fileInfo = context.ParseResult.GetValueForOption(fileOption);
    var destination = context.ParseResult.GetValueForOption(destinationOption)?.FullName??Environment.CurrentDirectory;
    var unpack = context.ParseResult.GetValueForOption(unpackPackages);

    if(!Directory.Exists(destination))
    {
        Directory.CreateDirectory(destination);
    }

    AnsiConsole.MarkupLine($"[orange1]Downloading NuGet packages[/]");
    AnsiConsole.MarkupLine($"Package list: [orange1]{fileInfo!.FullName}[/]");
    AnsiConsole.MarkupLine($"Destination: [orange1]{destination}[/]");

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
            var downloadTask = context.AddTask("Downloading");
            var fileContents = await File.ReadAllLinesAsync(fileInfo.FullName);
            var packages = fileContents.Skip(1).Select(item => item.Split('\t'))
                .Select(item => new Package() { Id = item[0], Version = item[1], License = item[2] })
                .ToList();
            downloadTask.MaxValue = packages.Count;

            await foreach (var item in PackageHelper.DownloadPackages(packages, destination))
            {
                downloadTask.IsIndeterminate = item.Index == null;
                downloadTask.Value = (item.Index + 1) ?? 0;

                if (item.Exception != null)
                {
                    AnsiConsole.MarkupLine($"[red]Error downloading {item.PackageId}: {item.Exception.Message}[/]");
                }
                else if(unpack && item.FullPath != null)
                {
                    var newDir = Path.Combine(destination, item.PackageId.ToLowerInvariant().Replace(".nuget", ""));
                    if(!Directory.Exists(newDir))
                    {
                        Directory.CreateDirectory(newDir);
                        ZipFile.ExtractToDirectory(item.FullPath, newDir);
                    }
                }
            }

            AnsiConsole.MarkupLine($"Downloaded [orange1]{packages.Count}[/] packages");
        });
});

return await rootCommand.InvokeAsync(args);

