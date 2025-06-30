using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;

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
            _httpClient = new HttpClient();
            _httpClient.BaseAddress = new Uri(_config.BitbucketApiBaseUrl);
        }

        private async Task EnsureAuthenticatedAsync()
        {
            // If we already have a token, we can use it.
            // In a real-world scenario, you would also check for token expiration.
            if (!string.IsNullOrEmpty(_accessToken))
            {
                return;
            }

            // Get the token using the OAuth Client Credentials Grant flow
            using var authClient = new HttpClient();
            var authRequest = new HttpRequestMessage(HttpMethod.Post, "https://bitbucket.org/site/oauth2/access_token")
            {
                Content = new FormUrlEncodedContent(new[]
                {
                    new KeyValuePair<string, string>("grant_type", "client_credentials")
                })
            };

            var basicAuth = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"{_config.BitbucketConsumerKey}:{_config.BitbucketConsumerSecret}"));
            authClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", basicAuth);

            var authResponse = await authClient.SendAsync(authRequest);
            authResponse.EnsureSuccessStatusCode();

            var responseStream = await authResponse.Content.ReadAsStreamAsync();
            var tokenResponse = await JsonSerializer.DeserializeAsync<JsonElement>(responseStream);
            
            _accessToken = tokenResponse.GetProperty("access_token").GetString();

            // Set the Bearer token for subsequent API requests
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

        public async Task<string> GetCommitsAsync(string workspace, string repoSlug, string nextPageUrl = null)
        {
            await EnsureAuthenticatedAsync();
            
            // Use the next page URL if provided, otherwise construct the initial URL
            var url = !string.IsNullOrEmpty(nextPageUrl) 
                ? nextPageUrl 
                : $"repositories/{workspace}/{repoSlug}/commits";

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

        public async Task<string> GetPullRequestsAsync(string workspace, string repoSlug, DateTime? startDate, DateTime? endDate, string nextPageUrl = null)
        {
            await EnsureAuthenticatedAsync();

            var url = nextPageUrl;
            if (string.IsNullOrEmpty(url))
            {
                // Build the initial URL with date filtering
                var query = $"updated_on >= {startDate:yyyy-MM-ddTHH:mm:ssZ} AND updated_on <= {endDate:yyyy-MM-ddTHH:mm:ssZ}";
                url = $"repositories/{workspace}/{repoSlug}/pullrequests?q={Uri.EscapeDataString(query)}";
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
    }
}
