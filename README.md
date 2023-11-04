# Readme

This project contains utilities for working with NuGet packages. The following utilities are available:

* `NugetPackages.PrepareList` - prepares a list of relevant packages and stores the results in a file in the current directory.
* `NugetPackages.DownloadList` - downloads NuGet packages listed in an input file.

Execute utilities directly from `Program.cs` source code with `dotnet run --`, e.g.: `dotnet run -- -h`