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
            await EnsureAuthenticatedAsync();
            var url = !string.IsNullOrEmpty(nextPageUrl) ? nextPageUrl : $"workspaces/{workspace}/members";
            var response = await _httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStringAsync();
        }

        public async Task<string> GetRepositoriesAsync(string workspace, string nextPageUrl = null)
        {
            await EnsureAuthenticatedAsync();
            var url = !string.IsNullOrEmpty(nextPageUrl) ? nextPageUrl : $"repositories/{workspace}";
            var response = await _httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStringAsync();
        }
        
        public async Task<string> GetCommitsAsync(string workspace, string repoSlug, string nextPageUrl = null)
        {
            await EnsureAuthenticatedAsync();
            
            var url = !string.IsNullOrEmpty(nextPageUrl) 
                ? nextPageUrl 
                : $"repositories/{workspace}/{repoSlug}/commits";

            var response = await _httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStringAsync();
        }
        
        public async Task<string> GetPullRequestsAsync(string workspace, string repoSlug, DateTime? startDate, DateTime? endDate, string nextPageUrl = null)
        {
            await EnsureAuthenticatedAsync();

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

            var response = await _httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStringAsync();
        }

        public async Task<string> GetPullRequestCommitsAsync(string workspace, string repoSlug, int pullRequestId, string nextPageUrl = null)
        {
            await EnsureAuthenticatedAsync();
            var url = !string.IsNullOrEmpty(nextPageUrl)
                ? nextPageUrl
                : $"repositories/{workspace}/{repoSlug}/pullrequests/{pullRequestId}/commits";
            
            var response = await _httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStringAsync();
        }
        
        public async Task<string> GetCommitDiffAsync(string workspace, string repoSlug, string commitHash)
        {
            await EnsureAuthenticatedAsync();
            var response = await _httpClient.GetAsync($"repositories/{workspace}/{repoSlug}/diff/{commitHash}");
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStringAsync();
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
