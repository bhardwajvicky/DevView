using BBIntegration.Common;
using BBIntegration.Users; // Reusing PaginatedResponseDto
using BBIntegration.Utils;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace BBIntegration.Commits
{
    public class BitbucketCommitsService
    {
        private readonly BitbucketApiClient _apiClient;
        private readonly BitbucketConfig _config;
        private readonly DiffParserService _diffParser;
        private readonly string _connectionString;
        private readonly ILogger<BitbucketCommitsService> _logger;

        public BitbucketCommitsService(BitbucketConfig config, BitbucketApiClient apiClient, DiffParserService diffParser, ILogger<BitbucketCommitsService> logger)
        {
            _config = config;
            _apiClient = apiClient;
            _diffParser = diffParser;
            _connectionString = config.DbConnectionString;
            _logger = logger;
        }

        public async Task SyncCommitsAsync(string workspace, string repoSlug, DateTime startDate, DateTime endDate)
        {
            _logger.LogInformation("Starting commit sync for {Workspace}/{RepoSlug}", workspace, repoSlug);
            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();
            
            // Get the internal ID for the repository
            var repoId = await connection.QuerySingleOrDefaultAsync<int?>(
                "SELECT Id FROM Repositories WHERE Name = @repoSlug", new { repoSlug });

            if (repoId == null)
            {
                _logger.LogWarning("Repository '{RepoSlug}' not found. Sync repositories first.", repoSlug);
                return;
            }

            string nextPageUrl = null;
            var keepFetching = true;

            try
            {
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

                        // Check if the commit already exists
                        var existingCommit = await connection.QuerySingleOrDefaultAsync<(int Id, int? CodeLinesAdded, bool? IsMerge, bool? IsPRMergeCommit)>(
                            "SELECT Id, CodeLinesAdded, IsMerge, IsPRMergeCommit FROM Commits WHERE BitbucketCommitHash = @Hash", new { commit.Hash });
                        
                        // Determine if this is a merge commit using the new parents logic
                        bool isMergeCommit = commit.Parents != null && commit.Parents.Count >= 2;
                        // Most merge commits are PR merge commits, so set both flags to true for merge commits
                        bool isPRMergeCommit = isMergeCommit;
                        
                        if (existingCommit.Id > 0 && existingCommit.CodeLinesAdded.HasValue)
                        {
                            // Commit exists and is complete, but check if flags need updating
                            bool needsUpdate = false;
                            var updateFields = new List<string>();
                            var updateParams = new Dictionary<string, object> { { "Id", existingCommit.Id } };
                            
                            if (existingCommit.IsMerge != isMergeCommit)
                            {
                                updateFields.Add("IsMerge = @IsMerge");
                                updateParams["IsMerge"] = isMergeCommit;
                                needsUpdate = true;
                            }
                            
                            // Update IsPRMergeCommit based on merge status
                            if (existingCommit.IsPRMergeCommit != isPRMergeCommit)
                            {
                                updateFields.Add("IsPRMergeCommit = @IsPRMergeCommit");
                                updateParams["IsPRMergeCommit"] = isPRMergeCommit;
                                needsUpdate = true;
                            }
                            
                            if (needsUpdate)
                            {
                                var updateSql = $"UPDATE Commits SET {string.Join(", ", updateFields)} WHERE Id = @Id";
                                await connection.ExecuteAsync(updateSql, updateParams);
                                _logger.LogInformation("Updated flags for existing complete commit: {CommitHash} (IsMerge: {IsMerge}, IsPRMergeCommit: {IsPRMergeCommit})", 
                                    commit.Hash, isMergeCommit, isPRMergeCommit);
                            }
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
                                SET LinesAdded = @LinesAdded, LinesRemoved = @LinesRemoved, CodeLinesAdded = @CodeLinesAdded, CodeLinesRemoved = @CodeLinesRemoved, IsMerge = @IsMerge, IsPRMergeCommit = @IsPRMergeCommit
                                WHERE Id = @Id;
                            ";
                            await connection.ExecuteAsync(updateSql, new 
                            {
                                Id = existingCommit.Id,
                                LinesAdded = totalAdded,
                                LinesRemoved = totalRemoved,
                                CodeLinesAdded = codeAdded,
                                CodeLinesRemoved = codeRemoved,
                                IsMerge = isMergeCommit,
                                IsPRMergeCommit = isPRMergeCommit
                            });
                            _logger.LogInformation("Updated commit: {CommitHash} (IsMerge: {IsMerge}, IsPRMergeCommit: {IsPRMergeCommit})", 
                                commit.Hash, isMergeCommit, isPRMergeCommit);
                        }
                        else
                        {
                            // INSERT the new commit
                            // Find or insert the author's internal ID
                            int? authorId = null;
                            string displayName = null, email = null, bitbucketUserId = null;
                            if (commit.Author?.User?.Uuid != null)
                            {
                                bitbucketUserId = commit.Author.User.Uuid;
                                authorId = await connection.QuerySingleOrDefaultAsync<int?>(
                                    "SELECT Id FROM Users WHERE BitbucketUserId = @Uuid", new { Uuid = bitbucketUserId });
                            }
                            if (authorId == null)
                            {
                                // Try to parse from raw
                                var raw = commit.Author?.Raw;
                                if (!string.IsNullOrEmpty(raw))
                                {
                                    var match = Regex.Match(raw, @"^(.*?)\s*<(.+?)>$");
                                    if (match.Success)
                                    {
                                        displayName = match.Groups[1].Value;
                                        email = match.Groups[2].Value;
                                    }
                                    else
                                    {
                                        displayName = raw;
                                    }
                                }
                                if (string.IsNullOrEmpty(bitbucketUserId) && !string.IsNullOrEmpty(email))
                                {
                                    bitbucketUserId = $"synthetic:{email}";
                                }
                                if (string.IsNullOrEmpty(bitbucketUserId))
                                {
                                    // Fallback: use commit hash as synthetic user ID
                                    bitbucketUserId = $"synthetic:unknown:{commit.Hash}";
                                }
                                if (string.IsNullOrEmpty(displayName))
                                {
                                    displayName = "Unknown";
                                }
                                if (!string.IsNullOrEmpty(bitbucketUserId))
                                {
                                    // Insert user if not exists
                                    const string insertUserSql = @"
                                        IF NOT EXISTS (SELECT 1 FROM Users WHERE BitbucketUserId = @BitbucketUserId)
                                        BEGIN
                                            INSERT INTO Users (BitbucketUserId, DisplayName, AvatarUrl, CreatedOn)
                                            VALUES (@BitbucketUserId, @DisplayName, NULL, @CreatedOn);
                                        END
                                        SELECT Id FROM Users WHERE BitbucketUserId = @BitbucketUserId;
                                    ";
                                    authorId = await connection.QuerySingleOrDefaultAsync<int?>(insertUserSql, new
                                    {
                                        BitbucketUserId = bitbucketUserId,
                                        DisplayName = displayName,
                                        CreatedOn = commit.Date
                                    });
                                }
                            }
                            if (authorId == null)
                            {
                                _logger.LogWarning(
                                    "Author for commit '{CommitHash}' not found and could not be created. Raw: '{Raw}', User UUID: '{Uuid}', DisplayName: '{DisplayName}', Email: '{Email}', BitbucketUserId: '{BitbucketUserId}'. Skipping commit insert.",
                                    commit.Hash, commit.Author?.Raw, commit.Author?.User?.Uuid, displayName, email, bitbucketUserId);
                                continue;
                            }
                            // Use the pre-calculated merge flag
                            const string insertSql = @"
                                INSERT INTO Commits (BitbucketCommitHash, RepositoryId, AuthorId, Date, Message, LinesAdded, LinesRemoved, IsMerge, CodeLinesAdded, CodeLinesRemoved, IsPRMergeCommit)
                                VALUES (@Hash, @RepoId, @AuthorId, @Date, @Message, @LinesAdded, @LinesRemoved, @IsMerge, @CodeLinesAdded, @CodeLinesRemoved, @IsPRMergeCommit);
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
                                IsMerge = isMergeCommit,
                                CodeLinesAdded = codeAdded,
                                CodeLinesRemoved = codeRemoved,
                                IsPRMergeCommit = isPRMergeCommit
                            });
                            _logger.LogInformation("Added commit: {CommitHash} (IsMerge: {IsMerge}, IsPRMergeCommit: {IsPRMergeCommit})", 
                                commit.Hash, isMergeCommit, isPRMergeCommit);
                        }
                    }

                    // Prepare for the next page
                    nextPageUrl = pagedResponse.NextPageUrl;
                    if (string.IsNullOrEmpty(nextPageUrl)) keepFetching = false;
                }

                _logger.LogInformation("Commit sync finished for {Workspace}/{RepoSlug}", workspace, repoSlug);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred during commit sync for {Workspace}/{RepoSlug}", workspace, repoSlug);
                throw;
            }
        }
    }
}
