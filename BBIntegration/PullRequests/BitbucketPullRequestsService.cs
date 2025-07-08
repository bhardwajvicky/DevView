using BBIntegration.Common;
using BBIntegration.Commits; // Reusing CommitDto
using BBIntegration.Users;   // Reusing PaginatedResponseDto
using BBIntegration.Utils;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace BBIntegration.PullRequests
{
    public class BitbucketPullRequestsService
    {
        private readonly BitbucketApiClient _apiClient;
        private readonly string _connectionString;
        private readonly ILogger<BitbucketPullRequestsService> _logger;
        private readonly DiffParserService _diffParser;

        public BitbucketPullRequestsService(BitbucketApiClient apiClient, BitbucketConfig config, ILogger<BitbucketPullRequestsService> logger, DiffParserService diffParser)
        {
            _apiClient = apiClient;
            _connectionString = config.DbConnectionString;
            _logger = logger;
            _diffParser = diffParser;
        }

        public async Task<bool> SyncPullRequestsAsync(string workspace, string repoSlug, DateTime? startDate, DateTime? endDate)
        {
            _logger.LogInformation("Starting PR sync for {Workspace}/{RepoSlug} from {StartDate:yyyy-MM-dd} to {EndDate:yyyy-MM-dd}", workspace, repoSlug, startDate, endDate);
            
            bool hitStartDateBoundary = false; // Indicates if we found PRs older than startDate

            // Check if we're currently rate limited
            if (BitbucketApiClient.IsRateLimited())
            {
                var waitTime = BitbucketApiClient.GetRateLimitWaitTime();
                _logger.LogWarning("API is currently rate limited. PR sync will wait {WaitTime} seconds before starting.", waitTime?.TotalSeconds ?? 0);
            }
            
            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            var repoId = await connection.QuerySingleOrDefaultAsync<int?>(
                "SELECT Id FROM Repositories WHERE Name = @repoSlug", new { repoSlug });

            if (repoId == null)
            {
                _logger.LogWarning("Repository '{RepoSlug}' not found. Sync repositories first.", repoSlug);
                return false;
            }

            string nextPageUrl = null;
            var keepFetching = true;
            try
            {
                while(keepFetching)
                {
                    // Check for rate limiting before each API call
                    if (BitbucketApiClient.IsRateLimited())
                    {
                        var waitTime = BitbucketApiClient.GetRateLimitWaitTime();
                        _logger.LogInformation("Waiting for rate limit to reset ({WaitTime} seconds) before fetching pull requests...", waitTime?.TotalSeconds ?? 0);
                    }
                    
                    var prsJson = await _apiClient.GetPullRequestsAsync(workspace, repoSlug, startDate, endDate, nextPageUrl);
                    //_logger.LogInformation("Raw PRs JSON: {Json}", prsJson);
                    var prPagedResponse = JsonSerializer.Deserialize<PaginatedResponseDto<PullRequestDto>>(prsJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                    if (prPagedResponse?.Values == null || !prPagedResponse.Values.Any()) {
                        keepFetching = false;
                        break;
                    }

                    foreach (var pr in prPagedResponse.Values)
                    {
                        if (pr.CreatedOn < startDate)
                        {
                            hitStartDateBoundary = true;
                            keepFetching = false; // Stop fetching more pages for this specific repo within this batch
                            break; // Exit foreach loop
                        }

                        if (pr.CreatedOn > endDate) continue;

                        if (pr.Author?.Uuid == null)
                        {
                            _logger.LogWarning("PR '{PrId}' has no author or author UUID. Skipping.", pr.Id);
                            continue;
                        }

                        // Find the author's internal ID
                        var authorId = await connection.QuerySingleOrDefaultAsync<int?>(
                            "SELECT Id FROM Users WHERE BitbucketUserId = @Uuid", new { Uuid = pr.Author.Uuid });

                        if (authorId == null)
                        {
                            _logger.LogWarning("Author with UUID '{AuthorUuid}' not found for PR '{PrId}'. Sync users first.", pr.Author.Uuid, pr.Id);
                            continue;
                        }

                        // Insert or update the pull request
                        const string prSql = @"
                            MERGE INTO PullRequests AS Target
                            USING (SELECT @BitbucketPrId AS BitbucketPrId) AS Source
                            ON Target.BitbucketPrId = Source.BitbucketPrId
                            WHEN MATCHED THEN
                                UPDATE SET Title = @Title, State = @State, UpdatedOn = @UpdatedOn, MergedOn = @MergedOn
                            WHEN NOT MATCHED BY TARGET THEN
                                INSERT (BitbucketPrId, RepositoryId, AuthorId, Title, State, CreatedOn, UpdatedOn, MergedOn)
                                VALUES (@BitbucketPrId, @RepoId, @AuthorId, @Title, @State, @CreatedOn, @UpdatedOn, @MergedOn);
                            SELECT Id FROM PullRequests WHERE BitbucketPrId = @BitbucketPrId;
                        ";
                        var prDbId = await connection.QuerySingleAsync<int>(prSql, new
                        {
                            BitbucketPrId = pr.Id.ToString(),
                            RepoId = repoId.Value,
                            AuthorId = authorId.Value,
                            pr.Title,
                            pr.State,
                            pr.CreatedOn,
                            pr.UpdatedOn,
                            MergedOn = pr.State == "MERGED" ? (DateTime?)pr.UpdatedOn : null
                        });

                        // Now, sync commits for this PR
                        _logger.LogDebug("Starting commit sync for PR {PrId} ({PrTitle}) in {Workspace}/{RepoSlug}", pr.Id, pr.Title, workspace, repoSlug);
                        await SyncCommitsForPullRequest(connection, workspace, repoSlug, pr.Id, prDbId);
                    }
                    nextPageUrl = prPagedResponse.NextPageUrl;
                    if (string.IsNullOrEmpty(nextPageUrl)) keepFetching = false;
                } 
                
                _logger.LogInformation("PR sync finished for {Workspace}/{RepoSlug}", workspace, repoSlug);
                return hitStartDateBoundary; // Return true if we hit the boundary, meaning there's more history to fetch
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred during PR sync for {Workspace}/{RepoSlug}", workspace, repoSlug);
                throw;
            }
        }

        private async Task SyncCommitsForPullRequest(SqlConnection connection, string workspace, string repoSlug, int bitbucketPrId, int prDbId)
        {
            try
            {
                string commitNextPageUrl = null;
                do
                {
                    // Check for rate limiting before each API call
                    if (BitbucketApiClient.IsRateLimited())
                    {
                        var waitTime = BitbucketApiClient.GetRateLimitWaitTime();
                        _logger.LogInformation("Waiting for rate limit to reset ({WaitTime} seconds) before fetching PR commits for PR {PrId}...", waitTime?.TotalSeconds ?? 0, bitbucketPrId);
                    }
                    
                    var commitsJson = await _apiClient.GetPullRequestCommitsAsync(workspace, repoSlug, bitbucketPrId, commitNextPageUrl);
                    var commitPagedResponse = JsonSerializer.Deserialize<PaginatedResponseDto<CommitDto>>(commitsJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                    if (commitPagedResponse?.Values == null || !commitPagedResponse.Values.Any()) 
                    {
                        if (string.IsNullOrEmpty(commitNextPageUrl))
                        {
                            _logger.LogInformation("PR {PrId} in {Workspace}/{RepoSlug} has no commits. This is normal for empty or draft PRs.", bitbucketPrId, workspace, repoSlug);
                        }
                        break;
                    }
                
                foreach (var commit in commitPagedResponse.Values)
                {
                    // Find the repository ID (if not already available)
                    var repoId = await connection.QuerySingleOrDefaultAsync<int?>(
                        "SELECT Id FROM Repositories WHERE Slug = @RepoSlug", new { RepoSlug = repoSlug });
                    if (repoId == null) {
                        _logger.LogWarning("Repository '{RepoSlug}' not found when inserting commit '{CommitHash}'.", repoSlug, commit.Hash);
                        continue;
                    }

                    int commitId = await CommitCrudHelper.UpsertCommitAndFilesAsync(
                        connection,
                        commit,
                        repoId.Value,
                        workspace,
                        repoSlug,
                        _apiClient,
                        _diffParser,
                        _logger
                    );
                    if (commitId < 0) continue;

                    // Associate commit with the PR
                    const string joinSql = @"
                        IF NOT EXISTS (SELECT 1 FROM PullRequestCommits WHERE PullRequestId = @PrDbId AND CommitId = @CommitId)
                        BEGIN
                            INSERT INTO PullRequestCommits (PullRequestId, CommitId) VALUES (@PrDbId, @CommitId);
                        END
                    ";
                    await connection.ExecuteAsync(joinSql, new { PrDbId = prDbId, CommitId = commitId });
                }
                commitNextPageUrl = commitPagedResponse.NextPageUrl;
            } while (!string.IsNullOrEmpty(commitNextPageUrl));
            }
            catch (HttpRequestException ex) when (ex.Data.Contains("StatusCode") && ex.Data["StatusCode"].Equals(System.Net.HttpStatusCode.NotFound))
            {
                _logger.LogWarning("PR {PrId} in {Workspace}/{RepoSlug} has no accessible commits (404 error). This is normal for empty PRs, draft PRs, or PRs with deleted branches. Skipping commit sync for this PR.", bitbucketPrId, workspace, repoSlug);
            }
            catch (HttpRequestException ex) when (ex.Message.Contains("404") || ex.Message.Contains("Not Found"))
            {
                _logger.LogWarning("PR {PrId} in {Workspace}/{RepoSlug} has no accessible commits (404 error). This is normal for empty PRs, draft PRs, or PRs with deleted branches. Skipping commit sync for this PR.", bitbucketPrId, workspace, repoSlug);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to sync commits for PR {PrId} in {Workspace}/{RepoSlug}. This PR will be skipped.", bitbucketPrId, workspace, repoSlug);
            }
        }
    }
}
