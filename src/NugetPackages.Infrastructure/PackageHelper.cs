using System.Collections.Concurrent;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using NuGet.Common;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;
using NugetPackages.Model;
using static NuGet.Protocol.Core.Types.PackageSearchMetadataBuilder;

namespace NugetPackages.Infrastructure
{
    public static class PackageHelper
    {
        private static readonly SourceCacheContext cache = new();
        private static PackageMetadataResourceV3? packageMetadataResource;
        private static PackageSearchResourceV3? packageSearchResource;

        private const int TasksPerMinute = 900;
        private const int TasksPerSecond = TasksPerMinute / 60;
        private const int ConcurrentTasksLimit = 10;

        /// <param name="searchTerm">owner:aspnet</param>
        /// <returns></returns>
        /// <seealso cref="https://learn.microsoft.com/en-us/nuget/consume-packages/finding-and-choosing-packages#search-syntax"/>
        public static async Task<IList<ClonedPackageSearchMetadata>> SearchPackages(string searchTerm, int pageSize = 1000, CancellationToken cancellationToken = default)
        {
            var repository = Repository.Factory.GetCoreV3("https://api.nuget.org/v3/index.json");
            SearchFilter searchFilter = new(includePrerelease: true)
            {
                IncludeDelisted = false,
                SupportedFrameworks = new[] { "net6.0", "net7.0", "net8.0"/*, "netstandard2.0", "netstandard2.1", "netcoreapp3.1"*/ }
                //PackageTypes = new[] { "Dependency" },
            };

            packageSearchResource ??= (PackageSearchResourceV3)(await new PackageSearchResourceV3Provider().TryCreate(repository, cancellationToken)).Item2;

            List<IPackageSearchMetadata> results = new();

            var skip = 0;
            while (true)
            {
                var packages = await packageSearchResource.SearchAsync(
                    searchTerm,
                    searchFilter,
                    skip: skip,
                    take: pageSize,
                    log: NullLogger.Instance,
                    cancellationToken: cancellationToken
                );

                results.AddRange(packages);
                if (packages.Count() < pageSize)
                {
                    break;
                }

                skip += pageSize;
            }

            var toSkip = new List<string>
            {
                "Microsoft.AspNetCore.App.Runtime",
                "Microsoft.NETCore.App.Runtime",
                "Microsoft.NETCore.Runtime",
                "Microsoft.Build.Runtime",
                "Microsoft.NET.Runtime",
                "Microsoft.WindowsDesktop.App.Runtime",
                "nanoFramework.",
                "runtime.",
                "-cuda-",
                ".Emscripten.",
                ".Owin.",
                "Microsoft.Toolkit",
                "Silk.NET",
                "Microsoft.NET.Sdk",
                "Microsoft.NET.Tools.NETCoreCheck",
                "Microsoft.Net.ToolsetCompilers",
                ".mono.",
                "Microsoft.NETCore.App.Crossgen",
                "Microsoft.NETCore.App.Host",
                "Microsoft.NETFramework.ReferenceAssemblies",
                "Microsoft.NETCore.DotNetAppHost",
                "Microsoft.NETCore.DotNetHost",
            };

            return results
                .ConvertAll(item => (ClonedPackageSearchMetadata)item)
                .Where(item => !toSkip.Any(x => item.Identity.Id.ToLowerInvariant().Contains(x.ToLowerInvariant())))
                .Where(item => !(item.Description?.ToLowerInvariant()?.Contains("do not reference directly") ?? false))
                .ToList();
        }


        public static async Task<IList<PackageSearchMetadataRegistration>> GetPackageMetadatas(string packageId, CancellationToken cancellationToken = default)
        {
            var repository = Repository.Factory.GetCoreV3("https://api.nuget.org/v3/index.json");

            packageMetadataResource ??= (PackageMetadataResourceV3)(await new PackageMetadataResourceV3Provider().TryCreate(repository, cancellationToken)).Item2;

            IList<PackageSearchMetadataRegistration> packages = (await packageMetadataResource.GetMetadataAsync(
                packageId,
                includePrerelease: true,
                includeUnlisted: false,
                cache,
                NullLogger.Instance,
                cancellationToken))
                .ToList()
                .ConvertAll(item => (PackageSearchMetadataRegistration)item);

            packages = packages.OrderByDescending(item => item.Version).ToList();

            // determine if a package follows the same versioning pattern as .net
            if (packages.Any(item => item.Version.Major == 8)
                && packages.Any(item => item.Version.Major == 7)
                && packages.Any(item => item.Version.Major == 6)
                && new string[]{"dotnet", "microsoft", "system", "runtime" }.Any(item => packageId.ToLowerInvariant().StartsWith(item)))
            {
                // take one of each latest .net version
                return new List<PackageSearchMetadataRegistration>
                {
                    packages.FirstOrDefault(item => item.Version.Major == 8 && !item.Version.IsPrerelease)
                    ?? packages.First(item => item.Version.Major == 8),
                    packages.FirstOrDefault(item => item.Version.Major == 7 && !item.Version.IsPrerelease)
                    ?? packages.First(item => item.Version.Major == 7),
                    packages.FirstOrDefault(item => item.Version.Major == 6 && !item.Version.IsPrerelease)
                    ?? packages.First(item => item.Version.Major == 6)
                };                    
            }

            var releases = packages.Where(item => !item.Version.ToString().Contains('-'));

            var versionsToTake = 1;

            // take last x releases, or anything if there are no releases
            return releases.Any() ? releases.Take(versionsToTake).ToList() : (IList<PackageSearchMetadataRegistration>)packages.Take(versionsToTake).ToList();
        }

        public static async IAsyncEnumerable<PackageProcessingResult> ProcessSearchPackagesResult(IList<ClonedPackageSearchMetadata> searchResult, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            // yield initial result to indicate processing has started
            yield return new PackageProcessingResult()
            {
                Count = searchResult.Count
            };

            using SemaphoreSlim rateLimiter = new(TasksPerSecond);
            using SemaphoreSlim concurrentLimiter = new(ConcurrentTasksLimit);
            ConcurrentQueue<Func<Task>> taskFunctionQueue = new();
            ConcurrentQueue<PackageProcessingResult> results = new();

            void Timer_Elapsed(object? sender, System.Timers.ElapsedEventArgs e)
            {
                _ = rateLimiter.Release(TasksPerSecond);
            }

            using System.Timers.Timer timer = new(1000);
            timer.AutoReset = true;
            timer.Elapsed += Timer_Elapsed;
            timer.Enabled = true;

            for (var i = 0; i < searchResult.Count; i++)
            {
                var packageId = searchResult[i].Identity.Id;
                var deprecation = await searchResult[i].GetDeprecationMetadataAsync();
                var isListed = searchResult[i].IsListed;
                taskFunctionQueue.Enqueue(() => WithRetry(async () =>
                {
                    try
                    {
                        if (deprecation != null
                        || !isListed)
                        {
                            PackageProcessingResult deprecatedResult = new()
                            {
                                Count = searchResult.Count,
                                PackageId = packageId
                            };

                            results.Enqueue(deprecatedResult);
                        }
                        else
                        {

                            var metadatas = await GetPackageMetadatas(packageId, cancellationToken: cancellationToken);

                            List<Package> packages = new();
                            foreach (var metadata in metadatas)
                            {
                                Package package = new()
                                {
                                    Id = metadata.Identity.Id,
                                    Version = metadata.Version.ToString(),
                                    License = metadata.LicenseExpression,
                                    Dependencies = metadata.DependencySets
                                                    .SelectMany(item => item.Packages)
                                                    .Select(item => item.Id).ToList()

                                };

                                packages.Add(package);
                            }

                            PackageProcessingResult result = new()
                            {
                                Count = searchResult.Count,
                                PackageVersions = packages,
                                PackageId = packageId
                            };

                            results.Enqueue(result);
                        }
                    }
                    catch (Exception ex)
                    {
                        PackageProcessingResult result = new()
                        {
                            Count = searchResult.Count,
                            PackageId = packageId,
                            Exception = ex
                        };

                        results.Enqueue(result);
                    }
                }));
            }

            var index = 0;
            while (!taskFunctionQueue.IsEmpty || !results.IsEmpty || concurrentLimiter.CurrentCount != ConcurrentTasksLimit)
            {
                while (results.TryDequeue(out var item))
                {
                    item.Index = index;
                    yield return item;
                    index++;
                }

                if (taskFunctionQueue.TryDequeue(out var taskFunction))
                {
                    await rateLimiter.WaitAsync(cancellationToken);
                    await concurrentLimiter.WaitAsync(cancellationToken);

                    var processingTask = Task.Run(taskFunction, cancellationToken)
                        .ContinueWith(t => concurrentLimiter.Release(), cancellationToken);
                }
            }

            timer.Enabled = false;
            yield break;
        }

        public static StringBuilder ProcessPackagesAsString(IList<Package> packages)
        {
            StringBuilder stringBuilder = new();
            _ = stringBuilder.AppendLine($"Id\tVersion\tLicense");

            AppendPackagesAsString(stringBuilder, packages);

            return stringBuilder;
        }

        public static void AppendPackagesAsString(StringBuilder stringBuilder, IList<Package> packages)
        {
            foreach (var result in packages)
            {
                _ = stringBuilder.AppendLine($"{result.Id}\t{result.Version}\t{result.License}");
            }
        }

        public static async IAsyncEnumerable<PackageDownloadResult> DownloadPackages(IList<Package> packages, string destinationPath, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            // yield initial result to indicate processing has started
            yield return new PackageDownloadResult()
            {
                Count = packages.Count
            };

            SourceRepository repository = Repository.Factory.GetCoreV3("https://api.nuget.org/v3/index.json");
            FindPackageByIdResource resource = await repository.GetResourceAsync<FindPackageByIdResource>();

            using SemaphoreSlim rateLimiter = new(TasksPerSecond);
            using SemaphoreSlim concurrentLimiter = new(ConcurrentTasksLimit);
            ConcurrentQueue<Func<Task>> taskFunctionQueue = new();
            ConcurrentQueue<PackageDownloadResult> results = new();

            void Timer_Elapsed(object? sender, System.Timers.ElapsedEventArgs e)
            {
                _ = rateLimiter.Release(TasksPerSecond);
            }

            using System.Timers.Timer timer = new(1000);
            timer.AutoReset = true;
            timer.Elapsed += Timer_Elapsed;
            timer.Enabled = true;

            for (var i = 0; i < packages.Count; i++)
            {
                var packageId = packages[i].Id;
                var packageIdWithVersion = packages[i].IdWithVersion;
                var packageVersion = new NuGetVersion(packages[i].Version);

                taskFunctionQueue.Enqueue(() => WithRetry(async () =>
                {
                    try
                    {
                        string filePath = Path.Combine(destinationPath, $"{packageIdWithVersion}.nupkg");
                        using FileStream packageStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None);

                        await resource.CopyNupkgToStreamAsync(
                            packageId,
                            packageVersion,
                            packageStream,
                            cache,
                            NullLogger.Instance,
                            cancellationToken);

                        PackageDownloadResult result = new()
                        {
                            Count = packages.Count,
                            PackageId = packageId,
                            FullPath = filePath
                        };

                        results.Enqueue(result);

                    }
                    catch (Exception ex)
                    {
                        PackageDownloadResult result = new()
                        {
                            Count = packages.Count,
                            PackageId = packageId,
                            Exception = ex
                        };

                        results.Enqueue(result);
                    }
                }));
            }

            var index = 0;
            while (!taskFunctionQueue.IsEmpty || !results.IsEmpty || concurrentLimiter.CurrentCount != ConcurrentTasksLimit)
            {
                while (results.TryDequeue(out var item))
                {
                    item.Index = index;
                    yield return item;
                    index++;
                }

                if (taskFunctionQueue.TryDequeue(out var taskFunction))
                {
                    await rateLimiter.WaitAsync(cancellationToken);
                    await concurrentLimiter.WaitAsync(cancellationToken);

                    var processingTask = Task.Run(taskFunction, cancellationToken)
                        .ContinueWith(t => concurrentLimiter.Release(), cancellationToken);
                }
            }

            timer.Enabled = false;
            yield break;
        }

        public static async Task WithRetry(Func<Task> func)
        {
            var hasRetried = false;

RETRY:

            try
            {
                await func();
            }
            catch
            {
                if (!hasRetried)
                {
                    hasRetried = true;
                    goto RETRY;
                }

                throw;
            }
        }
    }
}
