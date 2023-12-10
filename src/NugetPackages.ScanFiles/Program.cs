using System.CommandLine;
using NugetPackages.Infrastructure;
using Spectre.Console;
using Color = Spectre.Console.Color;

var directoryOption = new Option<DirectoryInfo>(
                new[] { "-d" },
                "Directory with files to scan. Recursion is not supported."
            )
{
    IsRequired = false
};
directoryOption.SetDefaultValue(new DirectoryInfo(Environment.CurrentDirectory));

var vtApiKeyOption = new Option<string>(
            new[] { "-k" },
            "VirusTotal API key.")
{
    IsRequired = true
};

var rootCommand = new RootCommand("Scans NuGet packages for threats using VirusTotal API.")
{
    directoryOption,
    vtApiKeyOption
};

rootCommand.SetHandler(async (context) =>
{
    var directory = context.ParseResult.GetValueForOption(directoryOption)!;
    var vtApiKey = context.ParseResult.GetValueForOption(vtApiKeyOption)!;
        
    AnsiConsole.MarkupLine($"[orange1]Scanning packages[/]");
    AnsiConsole.MarkupLine($"-d: [blue]{directory.FullName}[/]");

    if (string.IsNullOrWhiteSpace(vtApiKey))
    {
        throw new InvalidOperationException($"VirusTotal API key is required");
    }

    if (!directory.Exists)
    {
        throw new InvalidOperationException($"Directory {directory.FullName} does not exist");
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
            using var taApi = new VirusTotalApi(vtApiKey);
            var masks = new[] { "*.nupkg", "*.rpt" };

            // find all files which don't yet have a report
            var groupedFilesToScan = masks.SelectMany(m => directory.EnumerateFiles(m,SearchOption.TopDirectoryOnly))
                .GroupBy(file => Path.GetFileNameWithoutExtension(file.FullName))
                .Where(group => !group.Any(f => f.Extension == ".rpt"))
                .Select(group => group.Single())
                .ToList();

            AnsiConsole.MarkupLine($"[orange1]Found {groupedFilesToScan.Count} file(s) to scan[/]");

            if (groupedFilesToScan.Count > 0)
            {
                var scanTask = context.AddTask("Scanning");
                scanTask.MaxValue = groupedFilesToScan.Count;
                scanTask.IsIndeterminate = true;
                scanTask.Value = 0;

                foreach (var fileToScan in groupedFilesToScan)
                {
                    scanTask.IsIndeterminate = false;

                    try
                    {
                        string sha256 = await GeneralHelper.ComputeSha256ForFile(fileToScan);
                        var report = await taApi.GetFileReport(sha256);

                        if (report == null)
                        {
                            AnsiConsole.MarkupLine($"Report does yet exist for file [orange1]{fileToScan.Name}[/], uploading it");

                            await taApi.UploadFile(await File.ReadAllBytesAsync(fileToScan.FullName), fileToScan.Name);
                        }
                        else
                        {
                            await File.WriteAllTextAsync(Path.Combine(fileToScan.DirectoryName ?? "", Path.GetFileNameWithoutExtension(fileToScan.FullName) + ".rpt"), report.ToJsonString());
                            AnsiConsole.MarkupLine($"Scanned [orange1]{fileToScan.Name}[/]");
                        }
                    }
                    catch (Exception ex)
                    {
                        AnsiConsole.MarkupLine($"[red]Error scanning {fileToScan.FullName}: {ex.Message}[/]");
                    }

                    scanTask.Value++;
                }

                AnsiConsole.MarkupLine($"[orange1]Done[/]");
            }
        });
});

return await rootCommand.InvokeAsync(args);

