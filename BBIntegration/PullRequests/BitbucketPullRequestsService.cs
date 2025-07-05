using BBIntegration.Common;
using BBIntegration.Commits; // Reusing CommitDto
using BBIntegration.Users;   // Reusing PaginatedResponseDto
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace BBIntegration.PullRequests
{
    public class BitbucketPullRequestsService
    {
        private readonly BitbucketApiClient _apiClient;
        private readonly string _connectionString;
        private readonly ILogger<BitbucketPullRequestsService> _logger;

        public BitbucketPullRequestsService(BitbucketApiClient apiClient, BitbucketConfig config, ILogger<BitbucketPullRequestsService> logger)
        {
            _apiClient = apiClient;
            _connectionString = config.DbConnectionString;
            _logger = logger;
        }

        public async Task SyncPullRequestsAsync(string workspace, string repoSlug, DateTime? startDate, DateTime? endDate)
        {
            _logger.LogInformation("Starting PR sync for {Workspace}/{RepoSlug}", workspace, repoSlug);
            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            var repoId = await connection.QuerySingleOrDefaultAsync<int?>(
                "SELECT Id FROM Repositories WHERE Name = @repoSlug", new { repoSlug });

            if (repoId == null)
            {
                _logger.LogWarning("Repository '{RepoSlug}' not found. Sync repositories first.", repoSlug);
                return;
            }

            string nextPageUrl = null;
            try
            {
                do
                {
                    var prsJson = await _apiClient.GetPullRequestsAsync(workspace, repoSlug, startDate, endDate, nextPageUrl);
                    var prPagedResponse = JsonSerializer.Deserialize<PaginatedResponseDto<PullRequestDto>>(prsJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                    if (prPagedResponse?.Values == null || !prPagedResponse.Values.Any()) break;

                    foreach (var pr in prPagedResponse.Values)
                    {
                        if (pr.Author?.User?.Uuid == null)
                        {
                            _logger.LogWarning("PR '{PrId}' has no author or author UUID. Skipping.", pr.Id);
                            continue;
                        }

                        // Find the author's internal ID
                        var authorId = await connection.QuerySingleOrDefaultAsync<int?>(
                            "SELECT Id FROM Users WHERE BitbucketUserId = @Uuid", new { Uuid = pr.Author.User.Uuid });

                        if (authorId == null)
                        {
                            _logger.LogWarning("Author with UUID '{AuthorUuid}' not found for PR '{PrId}'. Sync users first.", pr.Author.User.Uuid, pr.Id);
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
                        await SyncCommitsForPullRequest(connection, workspace, repoSlug, pr.Id, prDbId);
                    }
                    nextPageUrl = prPagedResponse.NextPageUrl;
                } while (!string.IsNullOrEmpty(nextPageUrl));
                
                _logger.LogInformation("PR sync finished for {Workspace}/{RepoSlug}", workspace, repoSlug);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred during PR sync for {Workspace}/{RepoSlug}", workspace, repoSlug);
                throw;
            }
        }

        private async Task SyncCommitsForPullRequest(SqlConnection connection, string workspace, string repoSlug, int bitbucketPrId, int prDbId)
        {
            string commitNextPageUrl = null;
            do
            {
                var commitsJson = await _apiClient.GetPullRequestCommitsAsync(workspace, repoSlug, bitbucketPrId, commitNextPageUrl);
                var commitPagedResponse = JsonSerializer.Deserialize<PaginatedResponseDto<CommitDto>>(commitsJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (commitPagedResponse?.Values == null || !commitPagedResponse.Values.Any()) break;
                
                foreach (var commit in commitPagedResponse.Values)
                {
                    // Find the internal ID of the commit
                    var commitId = await connection.QuerySingleOrDefaultAsync<int?>(
                        "SELECT Id FROM Commits WHERE BitbucketCommitHash = @Hash", new { commit.Hash });
                    
                    if (commitId.HasValue)
                    {
                        // Associate commit with the PR
                        const string joinSql = @"
                            IF NOT EXISTS (SELECT 1 FROM PullRequestCommits WHERE PullRequestId = @PrDbId AND CommitId = @CommitId)
                            BEGIN
                                INSERT INTO PullRequestCommits (PullRequestId, CommitId) VALUES (@PrDbId, @CommitId);
                            END
                        ";
                        await connection.ExecuteAsync(joinSql, new { PrDbId = prDbId, CommitId = commitId.Value });
                    }
                    else
                    {
                        _logger.LogWarning("Commit '{CommitHash}' not found in local DB for PR '{BitbucketPrId}'. Sync commits first.", commit.Hash, bitbucketPrId);
                    }
                }
                commitNextPageUrl = commitPagedResponse.NextPageUrl;
            } while (!string.IsNullOrEmpty(commitNextPageUrl));
        }
    }
}
