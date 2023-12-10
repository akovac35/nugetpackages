using Newtonsoft.Json;
using NugetPackages.Model;

namespace NugetPackages.Infrastructure
{
    public class VirusTotalApi: IDisposable
    {
        private readonly HttpClient httpClient;
        private readonly VtRateLimiter rateLimiter;
        private bool disposedValue;

        public VirusTotalApi(string apiKeys)
        {
            httpClient = new HttpClient();
            rateLimiter = new VtRateLimiter(apiKeys);
        }

        public async Task<string> UploadFile(byte[] file, string fileName, CancellationToken cancellationToken = default)
        {
            return await rateLimiter.WithRateLimiting(work: async (string apiKey) =>
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, "https://www.virustotal.com/api/v3/files");
                request.Headers.Add("x-apikey", apiKey);
                request.Headers.Add("accept", "application/json");

                var content = new MultipartFormDataContent();
                using var fileContent = new ByteArrayContent(file);
                content.Add(fileContent, "file", fileName);
                request.Content = content;

                var response = await httpClient.SendAsync(request, cancellationToken);
                response.EnsureSuccessStatusCode();
                var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

                return responseBody;
            }, weight: 1, cancellationToken: cancellationToken);
        }

        public async Task<VirusTotalFileReport?> GetFileReport(string fileId, CancellationToken cancellationToken = default)
        {
            return await rateLimiter.WithRateLimiting(work: async (string apiKey) => {
                using var request = new HttpRequestMessage(HttpMethod.Get, $"https://www.virustotal.com/api/v3/files/{fileId}");
                request.Headers.Add("x-apikey", apiKey);
                request.Headers.Add("accept", "application/json");

                var response = await httpClient.SendAsync(request, cancellationToken);

                if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    return null;
                }

                response.EnsureSuccessStatusCode();
                var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
                return JsonConvert.DeserializeObject<VirusTotalFileReport>(responseBody);
            }, weight: 1, cancellationToken: cancellationToken);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    rateLimiter.Dispose();
                    httpClient.Dispose();
                }

                disposedValue = true;
            }
        }

        public void Dispose()
        {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }

        private sealed class VtRateLimiter : IDisposable
        {
            Queue<KeyValuePair<string, ConcurrentRateLimiter>> rateLimiters = new();
            private bool disposedValue;

            public VtRateLimiter(string apiKeys)
            {
                ApiKeys = apiKeys.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

                foreach (var key in ApiKeys)
                {
#pragma warning disable CA2000 // Dispose objects before losing scope
                    rateLimiters.Enqueue(new (key, new ConcurrentRateLimiter() { MaxItemsPerDay = 500, MaxItemsPerMinute = 4 }) );
#pragma warning restore CA2000 // Dispose objects before losing scope
                }
            }

            public IList<string> ApiKeys { get; }

            public async Task<T> WithRateLimiting<T>(Func<string /*apiKey*/, Task<T>> work, int weight = 1, CancellationToken cancellationToken = default)
            {
                var workKey = Guid.NewGuid().ToString();

                START:
                if(rateLimiters.TryDequeue(out var rateLimiter))
                {
                    try
                    {
                        if (await rateLimiter.Value.TryStart(workKey, weight, cancellationToken))
                        {
                            try
                            {
                                return await work(rateLimiter.Key);
                            }
                            finally
                            {
                                await rateLimiter.Value.End(workKey, cancellationToken);
                            }
                        }

                        await Task.Delay(10, cancellationToken);
                        goto START;
                    }
                    finally
                    {
                        rateLimiters.Enqueue(rateLimiter);
                    }
                }

                await Task.Delay(10, cancellationToken);
                goto START;
            }

            private void Dispose(bool disposing)
            {
                if (!disposedValue)
                {
                    if (disposing)
                    {
                        foreach (var rateLimiter in rateLimiters)
                        {
                            rateLimiter.Value.Dispose();
                        }
                    }

                    disposedValue = true;
                }
            }

            public void Dispose()
            {
                // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
                Dispose(disposing: true);
                GC.SuppressFinalize(this);
            }
        }
    }
}
