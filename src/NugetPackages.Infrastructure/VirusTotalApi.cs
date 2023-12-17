using Newtonsoft.Json;
using NugetPackages.Model;

namespace NugetPackages.Infrastructure
{
    public class VirusTotalApi: IDisposable
    {
        private readonly HttpClient httpClient;
        private readonly VtRateLimiter rateLimiter;
        private bool disposedValue;

        public VirusTotalApi(string apiKeys, VirusTotalApiConfig config)
        {
            httpClient = new HttpClient();
            rateLimiter = new VtRateLimiter(apiKeys, config);
        }

        public async Task<string> UploadFile(byte[] file, string fileName, CancellationToken cancellationToken = default)
        {
            if(file.Length > 1024*1024*32)
            {
                return await UploadLargeFile(file, fileName, cancellationToken);
            }
            
            return await rateLimiter.WithRateLimiting(work: async (string apiKey) =>
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, "https://www.virustotal.com/api/v3/files");
                request.Headers.Add("x-apikey", apiKey);
                request.Headers.Add("accept", "application/json");

                using var content = new MultipartFormDataContent();
                using var fileContent = new ByteArrayContent(file);
                content.Add(fileContent, "file", fileName);
                request.Content = content;

                var response = await httpClient.SendAsync(request, cancellationToken);
                response.EnsureSuccessStatusCode();
                var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

                return responseBody;
            }, weight: 1, cancellationToken: cancellationToken);
        }

        private async Task<string> UploadLargeFile(byte[] file, string fileName, CancellationToken cancellationToken = default)
        {
            return await rateLimiter.WithRateLimiting(async (string apiKey) =>
            {
                // Step 1: Request upload URL for large files
                using var requestForUploadUrl = new HttpRequestMessage(HttpMethod.Get, "https://www.virustotal.com/api/v3/files/upload_url");
                requestForUploadUrl.Headers.Add("x-apikey", apiKey);

                var responseForUploadUrl = await httpClient.SendAsync(requestForUploadUrl, cancellationToken);
                responseForUploadUrl.EnsureSuccessStatusCode();
                var uploadUrlResponseBody = await responseForUploadUrl.Content.ReadAsStringAsync(cancellationToken);
                var uploadUrl = JsonConvert.DeserializeObject<dynamic>(uploadUrlResponseBody)!.data.ToString();

                // Step 2: Upload the file to the received URL
                using var requestForFileUpload = new HttpRequestMessage(HttpMethod.Post, uploadUrl);
                requestForFileUpload.Headers.Add("x-apikey", apiKey);
                requestForFileUpload.Headers.Add("accept", "application/json");

                var multipartContent = new MultipartFormDataContent();
                var fileContent = new ByteArrayContent(file);
                fileContent.Headers.ContentDisposition = new System.Net.Http.Headers.ContentDispositionHeaderValue("form-data")
                {
                    Name = "\"file\"",
                    FileName = $"\"{fileName}\""
                };
                fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
                multipartContent.Add(fileContent, "file", fileName);
                requestForFileUpload.Content = multipartContent;

                var responseForFileUpload = await httpClient.SendAsync(requestForFileUpload, cancellationToken);
                responseForFileUpload.EnsureSuccessStatusCode();
                var fileUploadResponseBody = await responseForFileUpload.Content.ReadAsStringAsync(cancellationToken);

                return fileUploadResponseBody;
            }, weight: 2, cancellationToken: cancellationToken);
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

            public VtRateLimiter(string apiKeys, VirusTotalApiConfig config)
            {
                ApiKeys = apiKeys.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

                foreach (var key in ApiKeys)
                {
#pragma warning disable CA2000 // Dispose objects before losing scope
                    rateLimiters.Enqueue(new (key, new ConcurrentRateLimiter() { MaxItemsPerDay = config.RequestsPerDay, MaxItemsPerMinute = config.RequestsPerMinute }) );
#pragma warning restore CA2000 // Dispose objects before losing scope
                }
            }

            public IList<string> ApiKeys { get; }

            public async Task<T> WithRateLimiting<T>(Func<string /*apiKey*/, Task<T>> work, int weight = 1, CancellationToken cancellationToken = default)
            {
                var workKey = Guid.NewGuid().ToString();
                Dictionary<string, DateTime> nextEligibleUse = new ();

            START:
                if (rateLimiters.TryDequeue(out var rateLimiter))
                {
                    if (nextEligibleUse.TryGetValue(rateLimiter.Key, out var nextUseTime))
                    {
                        if(DateTime.UtcNow < nextUseTime)
                        {
                            // this rate limiter is currently paused, re-enqueue it and try the next one
                            rateLimiters.Enqueue(rateLimiter);
                            await Task.Delay(100, cancellationToken);
                            goto START;
                        }
                        else
                        {
                            nextEligibleUse.Remove(rateLimiter.Key);
                        }
                    }

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
                        else
                        {
                            // pause a bit before trying again
                            await Task.Delay(100, cancellationToken);
                        }
                    }
                    catch (HttpRequestException ex)
                    {
                        if (ex.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                        {
                            // stop using this rate limiter but continue using others
                            nextEligibleUse.Add(rateLimiter.Key, DateTime.UtcNow.AddMinutes(10));
                        }
                        else
                        {
                            throw;
                        }
                    }
                    finally
                    {
                        rateLimiters.Enqueue(rateLimiter);
                    }

                    goto START;
                }

                throw new NotImplementedException();
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
