// See https://aka.ms/new-console-template for more information
using System.CommandLine;
using System.IO.Compression;
using NugetPackages.Infrastructure;
using NugetPackages.Model;
using Spectre.Console;
using Color = Spectre.Console.Color;

var fileOption = new Option<FileInfo>(
                new[] { "-f" },
                "Path to the file containing a list of NuGet packages to download."
            )
{
    IsRequired = true
};

var directoryOption = new Option<DirectoryInfo>(
                new[] { "-d" },
                "Directory where NuGet packages will be stored. Defaults to: <current directory>"
            )
{
    IsRequired = false
};
directoryOption.SetDefaultValue(new DirectoryInfo(Environment.CurrentDirectory));

var unpackPackagesOption = new Option<bool>(
            new[] { "-u" },
            "Whether to unpack downloaded NuGet packages. Defaults to: false")
{
    IsRequired = false
};

var forceOption = new Option<bool>(
            new[] { "--force" },
            "Whether to process the package even if it is found in the directory. Defaults to: false")
{
    IsRequired = false
};

var rootCommand = new RootCommand("Downloads NuGet packages.")
            {
    fileOption,
    directoryOption,
    unpackPackagesOption,
    forceOption
};

rootCommand.SetHandler(async (context) =>
{
    var file = context.ParseResult.GetValueForOption(fileOption)!;
    var directory = context.ParseResult.GetValueForOption(directoryOption)!;
    var unpack = context.ParseResult.GetValueForOption(unpackPackagesOption);
    var force = context.ParseResult.GetValueForOption(forceOption);

    AnsiConsole.MarkupLine($"[orange1]Downloading NuGet packages[/]");
    AnsiConsole.MarkupLine($"-f: [blue]{file.FullName}[/]");
    AnsiConsole.MarkupLine($"-d: [blue]{directory.FullName}[/]");
    AnsiConsole.MarkupLine($"-u: [blue]{unpack}[/]");
    AnsiConsole.MarkupLine($"--force: [blue]{force}[/]");

    if (!directory.Exists)
    {
        Directory.CreateDirectory(directory.FullName);
    }

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
            var fileContents = await File.ReadAllLinesAsync(file.FullName);
            var packages = fileContents.Skip(1).Select(item => item.Split('\t'))
                .Select(item => new Package() { Id = item[0], Version = item[1], License = item[2] })
                .ToList();

            var filtererdPackages = packages;

            if(!force)
            {
                var dirFiles = directory.GetFiles("*.nupkg", SearchOption.TopDirectoryOnly)
                .Select(item => item.Name)
                .ToList();

                filtererdPackages = packages.Where(item => !dirFiles.Any(x => x.ToLowerInvariant() == $"{item.IdWithVersion}.nupkg".ToLowerInvariant()))
                .ToList();
            }

            downloadTask.MaxValue = packages.Count;

            await foreach (var item in PackageHelper.DownloadPackages(filtererdPackages, directory.FullName))
            {
                downloadTask.IsIndeterminate = item.Index == null;
                downloadTask.Value = (item.Index + 1) ?? 0;

                if (item.Exception != null)
                {
                    AnsiConsole.MarkupLine($"[red]Error downloading {item.PackageId}: {item.Exception.Message}[/]");
                }
            }

            if (unpack)
            {
                foreach (var item in packages)
                {
                    try
                    {
                        var newDir = Path.Combine(directory.FullName, item.IdWithVersion);
                        if (!Directory.Exists(newDir) || force)
                        {
                            ZipFile.ExtractToDirectory(Path.Combine(directory.FullName, $"{item.IdWithVersion}.nupkg"), newDir, overwriteFiles: true);
                        }
                    }
                    catch (Exception ex)
                    {
                        AnsiConsole.MarkupLine($"[red]Error unpacking {item.Id}: {ex.Message}[/]");
                    }
                } 
            }

            AnsiConsole.MarkupLine($"Downloaded [orange1]{packages.Count}[/] packages");
        });
});

return await rootCommand.InvokeAsync(args);

