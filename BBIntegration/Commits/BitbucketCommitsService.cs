using BBIntegration.Common;
using BBIntegration.Users; // Reusing PaginatedResponseDto
using Dapper;
using Microsoft.Data.SqlClient;
using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace BBIntegration.Commits
{
    public class BitbucketCommitsService
    {
        private readonly BitbucketApiClient _apiClient;
        private readonly BitbucketConfig _config;

        public BitbucketCommitsService(BitbucketConfig config, BitbucketApiClient apiClient)
        {
            _config = config;
            _apiClient = apiClient;
        }

        public async Task SyncCommitsAsync(string workspace, string repoSlug, DateTime startDate, DateTime endDate)
        {
            using var connection = new SqlConnection(_config.DbConnectionString);
            await connection.OpenAsync();
            
            // Get the internal ID for the repository
            var repoId = await connection.QuerySingleOrDefaultAsync<int?>(
                "SELECT Id FROM Repositories WHERE Name = @repoSlug", new { repoSlug });

            if (repoId == null)
            {
                Console.WriteLine($"Repository '{repoSlug}' not found in local DB. Sync repositories first.");
                return;
            }

            string nextPageUrl = null;
            var keepFetching = true;

            while (keepFetching)
            {
                var commitsJson = await _apiClient.GetCommitsAsync(workspace, repoSlug, nextPageUrl);
                var pagedResponse = JsonSerializer.Deserialize<PaginatedResponseDto<CommitDto>>(commitsJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (pagedResponse?.Values == null || !pagedResponse.Values.Any()) break;

                foreach (var commit in pagedResponse.Values)
                {
                    // Stop if we've gone past the start date of our desired range
                    if (commit.Date < startDate)
                    {
                        keepFetching = false;
                        break;
                    }

                    // Skip if the commit is outside our desired date range
                    if (commit.Date > endDate) continue;

                    // Check if the commit already exists in the database
                    var existingCommit = await connection.QuerySingleOrDefaultAsync<int?>(
                        "SELECT 1 FROM Commits WHERE BitbucketCommitHash = @Hash", new { commit.Hash });
                    
                    if (existingCommit.HasValue) continue;

                    // Fetch diff stats for the new commit
                    var diffStatJson = await _apiClient.GetCommitDiffStatAsync(workspace, repoSlug, commit.Hash);
                    var diffStats = JsonSerializer.Deserialize<PaginatedResponseDto<DiffStatDto>>(diffStatJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    
                    var linesAdded = diffStats.Values.Sum(s => s.LinesAdded);
                    var linesRemoved = diffStats.Values.Sum(s => s.LinesRemoved);

                    // Find the author's internal ID
                    var authorId = await connection.QuerySingleOrDefaultAsync<int?>(
                        "SELECT Id FROM Users WHERE BitbucketUserId = @Uuid", new { Uuid = commit.Author?.User?.Uuid });

                    if (authorId == null)
                    {
                        Console.WriteLine($"Author with UUID '{commit.Author?.User?.Uuid}' not found for commit '{commit.Hash}'. Sync users first.");
                        continue;
                    }

                    // Insert the new commit
                    const string sql = @"
                        INSERT INTO Commits (BitbucketCommitHash, RepositoryId, AuthorId, Date, Message, LinesAdded, LinesRemoved)
                        VALUES (@Hash, @RepoId, @AuthorId, @Date, @Message, @LinesAdded, @LinesRemoved);
                    ";
                    
                    await connection.ExecuteAsync(sql, new
                    {
                        commit.Hash,
                        RepoId = repoId.Value,
                        AuthorId = authorId.Value,
                        commit.Date,
                        commit.Message,
                        LinesAdded = linesAdded,
                        LinesRemoved = linesRemoved
                    });

                    Console.WriteLine($"Added commit: {commit.Hash}");
                }

                // Prepare for the next page
                nextPageUrl = pagedResponse.NextPageUrl;
                if (string.IsNullOrEmpty(nextPageUrl)) keepFetching = false;
            }

            Console.WriteLine("Commit synchronization complete.");
        }
    }
}
