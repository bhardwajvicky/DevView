using BBIntegration.Common;
using BBIntegration.Commits; // Reusing CommitDto
using BBIntegration.Users;   // Reusing PaginatedResponseDto
using Dapper;
using Microsoft.Data.SqlClient;
using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace BBIntegration.PullRequests
{
    public class BitbucketPullRequestsService
    {
        private readonly BitbucketApiClient _apiClient;
        private readonly BitbucketConfig _config;

        public BitbucketPullRequestsService(BitbucketConfig config, BitbucketApiClient apiClient)
        {
            _config = config;
            _apiClient = apiClient;
        }

        public async Task SyncPullRequestsAsync(string workspace, string repoSlug, DateTime startDate, DateTime endDate)
        {
            using var connection = new SqlConnection(_config.DbConnectionString);
            await connection.OpenAsync();

            var repoId = await connection.QuerySingleOrDefaultAsync<int?>(
                "SELECT Id FROM Repositories WHERE Name = @repoSlug", new { repoSlug });

            if (repoId == null)
            {
                Console.WriteLine($"Repository '{repoSlug}' not found. Sync repositories first.");
                return;
            }

            string prNextPageUrl = null;
            do
            {
                var prsJson = await _apiClient.GetPullRequestsAsync(workspace, repoSlug, startDate, endDate, prNextPageUrl);
                var prPagedResponse = JsonSerializer.Deserialize<PaginatedResponseDto<PullRequestDto>>(prsJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (prPagedResponse?.Values == null || !prPagedResponse.Values.Any()) break;

                foreach (var pr in prPagedResponse.Values)
                {
                    // Find Author ID
                    var authorId = await connection.QuerySingleOrDefaultAsync<int?>(
                        "SELECT Id FROM Users WHERE BitbucketUserId = @Uuid", new { Uuid = pr.Author?.Uuid });
                    if (authorId == null)
                    {
                        Console.WriteLine($"Author '{pr.Author?.Uuid}' not found for PR '{pr.Id}'. Sync users first.");
                        continue;
                    }

                    // Upsert the Pull Request
                    var prUpsertSql = @"
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
                    var localPrId = await connection.QuerySingleAsync<int>(prUpsertSql, new
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

                    // Sync commits associated with this PR
                    string commitNextPageUrl = null;
                    do
                    {
                        var commitsJson = await _apiClient.GetPullRequestCommitsAsync(workspace, repoSlug, pr.Id, commitNextPageUrl);
                        var commitPagedResponse = JsonSerializer.Deserialize<PaginatedResponseDto<CommitDto>>(commitsJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                        if (commitPagedResponse?.Values == null || !commitPagedResponse.Values.Any()) break;
                        
                        foreach (var commit in commitPagedResponse.Values)
                        {
                            var localCommitId = await connection.QuerySingleOrDefaultAsync<int?>(
                                "SELECT Id FROM Commits WHERE BitbucketCommitHash = @Hash", new { commit.Hash });
                            
                            if (localCommitId.HasValue)
                            {
                                // Link PR and Commit in the join table
                                var linkSql = @"
                                    IF NOT EXISTS (SELECT 1 FROM PullRequestCommits WHERE PullRequestId = @LocalPrId AND CommitId = @LocalCommitId)
                                    BEGIN
                                        INSERT INTO PullRequestCommits (PullRequestId, CommitId) VALUES (@LocalPrId, @LocalCommitId);
                                    END
                                ";
                                await connection.ExecuteAsync(linkSql, new { LocalPrId = localPrId, LocalCommitId = localCommitId.Value });
                            }
                        }
                        commitNextPageUrl = commitPagedResponse.NextPageUrl;
                    } while (!string.IsNullOrEmpty(commitNextPageUrl));
                }
                prNextPageUrl = prPagedResponse.NextPageUrl;
            } while (!string.IsNullOrEmpty(prNextPageUrl));

            Console.WriteLine("Pull request synchronization complete.");
        }
    }
}
