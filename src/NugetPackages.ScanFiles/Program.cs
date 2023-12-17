using System.CommandLine;
using System.Globalization;
using System.Text;
using Newtonsoft.Json;
using NugetPackages.Infrastructure;
using NugetPackages.Model;
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

var vtApiKeysOption = new Option<string>(
            new[] { "-k" },
            "VirusTotal API key(s). Multiple keys are separated with a comma.")
{
    IsRequired = true
};

var requestsPerMinuteOption = new Option<int>(
            new[] { "--rpm" },
            "Set maximum requests per minute per API key, assuming no prior requests.")
{
    IsRequired = false
};
requestsPerMinuteOption.SetDefaultValue(4);

var requestsPerDayOption = new Option<int>(
            new[] { "--rpd" },
            "Set maximum requests per day per API key, assuming no prior requests.")
{
    IsRequired = false
};
requestsPerDayOption.SetDefaultValue(500);

var pauseMinutesOption = new Option<int>(
            new[] { "--ptm" },
            "Set pause time (in minutes) for HTTP status code 429 - too many requests.")
{
    IsRequired = false
};
pauseMinutesOption.SetDefaultValue(10);

var maxReportAgeOption = new Option<int>(
            new[] { "--mra" },
            "Specify maximum VirusTotal report age, in days, after which file rescanning is requested.")
{
    IsRequired = false
};
maxReportAgeOption.SetDefaultValue(60);

var rootCommand = new RootCommand($"Scans NuGet packages (*.nupkg) for security threats by leveraging the VirusTotal API, generating .rpt reports and storing them in the same directory as the scanned package. Packages with unavailable hashes in VirusTotal are automatically uploaded for scanning. Note that this tool does not await the generation of reports once the package was uploaded; hence, multiple runs may be necessary until all reports are available locally. This approach also resolves any transient errors.")
{
    directoryOption,
    vtApiKeysOption,
    requestsPerMinuteOption,
    requestsPerDayOption,
    pauseMinutesOption,
    maxReportAgeOption
};

rootCommand.SetHandler(async (context) =>
{
    var directory = context.ParseResult.GetValueForOption(directoryOption)!;
    var vtApiKey = context.ParseResult.GetValueForOption(vtApiKeysOption)!;
    var perMinute = context.ParseResult.GetValueForOption(requestsPerMinuteOption)!;
    var perDay = context.ParseResult.GetValueForOption(requestsPerDayOption)!;
    var pauseMinutes = context.ParseResult.GetValueForOption(pauseMinutesOption)!;
    var maxReportAge = context.ParseResult.GetValueForOption(maxReportAgeOption)!;

    AnsiConsole.MarkupLine($"[orange1]Scanning packages[/]");
    AnsiConsole.MarkupLine($"-d:    [blue]{directory.FullName}[/]");
    AnsiConsole.MarkupLine($"--rpm: [blue]{perMinute}[/]");
    AnsiConsole.MarkupLine($"--rpd: [blue]{perDay}[/]");
    AnsiConsole.MarkupLine($"--ptm: [blue]{pauseMinutes}[/]");
    AnsiConsole.MarkupLine($"--mra: [blue]{maxReportAge}[/]");

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
            using var taApi = new VirusTotalApi(vtApiKey, new VirusTotalApiConfig() { RequestsPerMinute = perMinute, RequestsPerDay = perDay, PauseInMinutesForHttpStatusCode429 = pauseMinutes});
            var masks = new[] { "*.nupkg", "*.rpt" };

            // find all files which don't yet have a report
            var groupedFilesToScan = masks.SelectMany(m => directory.EnumerateFiles(m,SearchOption.TopDirectoryOnly))
                .GroupBy(file => Path.GetFileNameWithoutExtension(file.FullName))
                .Where(group => !group.Any(f => f.Extension == ".rpt"))
                .Select(group => group.Single())
                .ToList();

            AnsiConsole.MarkupLine($"[orange1]Found {groupedFilesToScan.Count} file(s) remaining to scan[/]");

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
            }

            // find all files which don't yet have a report
            var filesWithoutReport = masks.SelectMany(m => directory.EnumerateFiles(m, SearchOption.TopDirectoryOnly))
                .GroupBy(file => Path.GetFileNameWithoutExtension(file.FullName))
                .Where(group => !group.Any(f => f.Extension == ".rpt"))
                .Select(group => group.Single())
                .ToList();

            if (filesWithoutReport.Any())
            {
                AnsiConsole.MarkupLine($"[orange1]Found {filesWithoutReport.Count} file(s) remaining to scan - rerun the program[/]");

                return 1;
            }

            var reports = directory.EnumerateFiles("*.rpt", SearchOption.TopDirectoryOnly)
                .ToList();

            if (reports.Any())
            {
                var scanTask = context.AddTask("Processing reports");
                scanTask.MaxValue = reports.Count;
                scanTask.IsIndeterminate = true;
                scanTask.Value = 0;

                var threats = new List<FileInfo>();

                foreach (var report in reports)
                {
                    scanTask.IsIndeterminate = false;

                    try
                    {
                        var rptJson = await File.ReadAllTextAsync(report.FullName);
                        var rpt = JsonConvert.DeserializeObject<VirusTotalFileReport>(rptJson)!;

                        if (!rpt.IsOk())
                        {
                            threats.Add(report);
                            AnsiConsole.MarkupLine($"Report [red]{report.FullName}[/] indicates a threat or does not meet safety criteria");
                        }
                    }
                    catch (Exception ex)
                    {
                        AnsiConsole.MarkupLine($"[red]Error processing report {report.FullName}: {ex.Message}[/]");
                    }

                    scanTask.Value++;
                }

                StringBuilder scanResult = new();
                if (threats.Any())
                {
                    scanResult.AppendLine($"The following reports indicate threats or do not meet safety criteria:");
                    foreach (var threat in threats)
                    {
                        scanResult.AppendLine(threat.FullName);
                    }
                    scanResult.AppendLine();
                    scanResult.AppendLine($"Place the files in a different folder if manual checks disagree with automation.");
                }
                else
                {
                    scanResult.AppendLine($"No threats were found in scan reports.");
                }

                var scanResultFile = Path.Combine(directory.FullName, $"scan_result_{DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture)}.txt");
                await File.WriteAllTextAsync(scanResultFile, scanResult.ToString());

                if(threats.Any())
                {
                    AnsiConsole.MarkupLine($"[red]Threats were found, check scan result: {scanResultFile}[/]");
                    return 1;
                }
            }

            AnsiConsole.MarkupLine($"[orange1]No threats were found in scan reports[/]");
            return 0;
        });
});

return await rootCommand.InvokeAsync(args);

