using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;
using BBIntegration.Common;

namespace BBIntegration.Common
{
    public class BitbucketApiClient
    {
        private readonly HttpClient _httpClient;
        private readonly BitbucketConfig _config;
        private string _accessToken;

        public BitbucketApiClient(BitbucketConfig config)
        {
            _config = config;
            _httpClient = new HttpClient { BaseAddress = new Uri(_config.BitbucketApiBaseUrl) };
        }

        private async Task EnsureAuthenticatedAsync()
        {
            if (!string.IsNullOrEmpty(_accessToken)) return;

            var authClient = new HttpClient();
            var request = new HttpRequestMessage(HttpMethod.Post, "https://bitbucket.org/site/oauth2/access_token")
            {
                Content = new FormUrlEncodedContent(new[]
                {
                    new KeyValuePair<string, string>("grant_type", "client_credentials"),
                    new KeyValuePair<string, string>("client_id", _config.BitbucketConsumerKey),
                    new KeyValuePair<string, string>("client_secret", _config.BitbucketConsumerSecret)
                })
            };

            var response = await authClient.SendAsync(request);
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync();
            var tokenResponse = JsonSerializer.Deserialize<JsonElement>(content);
            _accessToken = tokenResponse.GetProperty("access_token").GetString();

            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
        }

        private async Task<string> SendRequestAsync(string url)
        {
            await EnsureAuthenticatedAsync();
            int maxRetries = 3;
            int retryCount = 0;
            TimeSpan delay = TimeSpan.FromSeconds(1); // Initial delay

            while (retryCount <= maxRetries)
            {
                try
                {
                    var response = await _httpClient.GetAsync(url);
                    if (response.IsSuccessStatusCode)
                    {
                        return await response.Content.ReadAsStringAsync();
                    }
                    else if (response.StatusCode == (System.Net.HttpStatusCode)429) // Too Many Requests
                    {
                        if (response.Headers.RetryAfter != null && response.Headers.RetryAfter.Delta.HasValue)
                        {
                            delay = response.Headers.RetryAfter.Delta.Value;
                        }
                        else
                        {
                            delay = TimeSpan.FromSeconds(Math.Pow(2, retryCount)); // Exponential backoff
                        }
                        Console.WriteLine($"Rate limit hit. Retrying in {delay.TotalSeconds} seconds...");
                        await Task.Delay(delay);
                        retryCount++;
                    }
                    else
                    {
                        response.EnsureSuccessStatusCode(); // Throw for other HTTP errors
                    }
                }
                catch (HttpRequestException ex)
                {
                    if (retryCount == maxRetries)
                    {
                        throw; // Re-throw if max retries reached
                    }

                    Console.WriteLine($"HTTP request failed: {ex.Message}. Retrying...");
                    await Task.Delay(delay);
                    retryCount++;
                }
            }
            throw new Exception("Failed to send request after multiple retries."); // Should not be reached
        }

        public async Task<string> GetCurrentUserAsync()
        {
            await EnsureAuthenticatedAsync();
            var response = await _httpClient.GetAsync("user");
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStringAsync();
        }

        public async Task<string> GetWorkspaceUsersAsync(string workspace)
        {
            await EnsureAuthenticatedAsync();
            var response = await _httpClient.GetAsync($"workspaces/{workspace}/members");
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStringAsync();
        }

        public async Task<string> GetWorkspaceRepositoriesAsync(string workspace)
        {
            await EnsureAuthenticatedAsync();
            var response = await _httpClient.GetAsync($"repositories/{workspace}");
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStringAsync();
        }

        public async Task<string> GetUsersAsync(string workspace, string nextPageUrl = null)
        {
            var url = !string.IsNullOrEmpty(nextPageUrl) ? nextPageUrl : $"workspaces/{workspace}/members";
            return await SendRequestAsync(url);
        }

        public async Task<string> GetRepositoriesAsync(string workspace, string nextPageUrl = null)
        {
            var url = !string.IsNullOrEmpty(nextPageUrl) ? nextPageUrl : $"repositories/{workspace}";
            return await SendRequestAsync(url);
        }
        
        public async Task<string> GetCommitsAsync(string workspace, string repoSlug, string nextPageUrl = null)
        {
            var url = !string.IsNullOrEmpty(nextPageUrl) 
                ? nextPageUrl 
                : $"repositories/{workspace}/{repoSlug}/commits";
            return await SendRequestAsync(url);
        }
        
        public async Task<string> GetPullRequestsAsync(string workspace, string repoSlug, DateTime? startDate, DateTime? endDate, string nextPageUrl = null)
        {
            var url = nextPageUrl;
            if (string.IsNullOrEmpty(url))
            {
                if (startDate.HasValue && endDate.HasValue)
                {
                    var query = $"updated_on >= {startDate:yyyy-MM-ddTHH:mm:ssZ} AND updated_on <= {endDate:yyyy-MM-ddTHH:mm:ssZ}";
                    url = $"repositories/{workspace}/{repoSlug}/pullrequests?q={Uri.EscapeDataString(query)}";
                }
                else
                {
                     url = $"repositories/{workspace}/{repoSlug}/pullrequests";
                }
            }
            return await SendRequestAsync(url);
        }

        public async Task<string> GetPullRequestCommitsAsync(string workspace, string repoSlug, int pullRequestId, string nextPageUrl = null)
        {
            var url = !string.IsNullOrEmpty(nextPageUrl)
                ? nextPageUrl
                : $"repositories/{workspace}/{repoSlug}/pullrequests/{pullRequestId}/commits";
            return await SendRequestAsync(url);
        }
        
        public async Task<string> GetCommitDiffAsync(string workspace, string repoSlug, string commitHash)
        {
            var url = $"repositories/{workspace}/{repoSlug}/diff/{commitHash}";
            return await SendRequestAsync(url);
        }

        private string BuildPullRequestsUrl(string workspace, string repoSlug, DateTime? startDate, DateTime? endDate)
        {
            if (startDate.HasValue && endDate.HasValue)
            {
                var query = $"updated_on >= {startDate:yyyy-MM-ddTHH:mm:ssZ} AND updated_on <= {endDate:yyyy-MM-ddTHH:mm:ssZ}";
                return $"repositories/{workspace}/{repoSlug}/pullrequests?q={Uri.EscapeDataString(query)}";
            }
            return null;
        }
    }
}
