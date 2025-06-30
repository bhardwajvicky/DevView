using BBIntegration.Common;
using BBIntegration.Users; // Reusing PaginatedResponseDto
using BBIntegration.Utils;
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
        private readonly DiffParserService _diffParser;

        public BitbucketCommitsService(BitbucketConfig config, BitbucketApiClient apiClient)
        {
            _config = config;
            _apiClient = apiClient;
            _diffParser = new DiffParserService();
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

                    // Check if the commit already exists and if it's incomplete
                    var existingCommit = await connection.QuerySingleOrDefaultAsync<(int Id, int? CodeLinesAdded)>(
                        "SELECT Id, CodeLinesAdded FROM Commits WHERE BitbucketCommitHash = @Hash", new { commit.Hash });
                    
                    if (existingCommit.Id > 0 && existingCommit.CodeLinesAdded.HasValue)
                    {
                        // Commit exists and is complete, so skip it
                        continue;
                    }

                    // Fetch raw diff and parse it
                    var diffContent = await _apiClient.GetCommitDiffAsync(workspace, repoSlug, commit.Hash);
                    var (totalAdded, totalRemoved, codeAdded, codeRemoved) = _diffParser.ParseDiff(diffContent);

                    if (existingCommit.Id > 0)
                    {
                        // UPDATE the existing, incomplete commit
                        const string updateSql = @"
                            UPDATE Commits 
                            SET LinesAdded = @LinesAdded, LinesRemoved = @LinesRemoved, CodeLinesAdded = @CodeLinesAdded, CodeLinesRemoved = @CodeLinesRemoved, IsMerge = @IsMerge
                            WHERE Id = @Id;
                        ";
                        await connection.ExecuteAsync(updateSql, new 
                        {
                            Id = existingCommit.Id,
                            LinesAdded = totalAdded,
                            LinesRemoved = totalRemoved,
                            CodeLinesAdded = codeAdded,
                            CodeLinesRemoved = codeRemoved,
                            IsMerge = commit.Message.Trim().StartsWith("Merge branch")
                        });
                        Console.WriteLine($"Updated commit: {commit.Hash}");
                    }
                    else
                    {
                        // INSERT the new commit
                        // Find the author's internal ID
                        var authorId = await connection.QuerySingleOrDefaultAsync<int?>(
                            "SELECT Id FROM Users WHERE BitbucketUserId = @Uuid", new { Uuid = commit.Author?.User?.Uuid });

                        if (authorId == null)
                        {
                            Console.WriteLine($"Author with UUID '{commit.Author?.User?.Uuid}' not found for commit '{commit.Hash}'. Sync users first.");
                            continue;
                        }
                        
                        const string insertSql = @"
                            INSERT INTO Commits (BitbucketCommitHash, RepositoryId, AuthorId, Date, Message, LinesAdded, LinesRemoved, IsMerge, CodeLinesAdded, CodeLinesRemoved)
                            VALUES (@Hash, @RepoId, @AuthorId, @Date, @Message, @LinesAdded, @LinesRemoved, @IsMerge, @CodeLinesAdded, @CodeLinesRemoved);
                        ";
                        
                        await connection.ExecuteAsync(insertSql, new
                        {
                            commit.Hash,
                            RepoId = repoId.Value,
                            AuthorId = authorId.Value,
                            commit.Date,
                            commit.Message,
                            LinesAdded = totalAdded,
                            LinesRemoved = totalRemoved,
                            IsMerge = commit.Message.Trim().StartsWith("Merge branch"),
                            CodeLinesAdded = codeAdded,
                            CodeLinesRemoved = codeRemoved
                        });
                        Console.WriteLine($"Added commit: {commit.Hash}");
                    }
                }

                // Prepare for the next page
                nextPageUrl = pagedResponse.NextPageUrl;
                if (string.IsNullOrEmpty(nextPageUrl)) keepFetching = false;
            }

            Console.WriteLine("Commit synchronization complete.");
        }
    }
}
