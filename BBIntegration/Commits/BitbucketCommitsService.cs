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
            
            // Check if we're currently rate limited
            if (BitbucketApiClient.IsRateLimited())
            {
                var waitTime = BitbucketApiClient.GetRateLimitWaitTime();
                _logger.LogWarning("API is currently rate limited. Sync will wait {WaitTime} seconds before starting.", waitTime?.TotalSeconds ?? 0);
            }
            
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
                    // Check for rate limiting before each API call
                    if (BitbucketApiClient.IsRateLimited())
                    {
                        var waitTime = BitbucketApiClient.GetRateLimitWaitTime();
                        _logger.LogInformation("Waiting for rate limit to reset ({WaitTime} seconds) before fetching commits...", waitTime?.TotalSeconds ?? 0);
                    }
                    
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
                            // Commit exists and is complete, skip further processing
                            continue;
                        }

                        // Check for rate limiting before diff API call
                        if (BitbucketApiClient.IsRateLimited())
                        {
                            var waitTime = BitbucketApiClient.GetRateLimitWaitTime();
                            _logger.LogInformation("Waiting for rate limit to reset ({WaitTime} seconds) before fetching diff for commit {CommitHash}...", waitTime?.TotalSeconds ?? 0, commit.Hash);
                        }
                        
                        // Fetch raw diff and parse it with file classification
                        var diffContent = await _apiClient.GetCommitDiffAsync(workspace, repoSlug, commit.Hash);
                        var diffSummary = _diffParser.ParseDiffWithClassification(diffContent);
                        
                        // Extract values for backward compatibility
                        var (totalAdded, totalRemoved, codeAdded, codeRemoved) = 
                            (diffSummary.TotalAdded, diffSummary.TotalRemoved, diffSummary.CodeAdded, diffSummary.CodeRemoved);

                        if (existingCommit.Id > 0)
                        {
                            // UPDATE the existing, incomplete commit
                            const string updateSql = @"
                                UPDATE Commits 
                                SET LinesAdded = @LinesAdded, LinesRemoved = @LinesRemoved, 
                                    CodeLinesAdded = @CodeLinesAdded, CodeLinesRemoved = @CodeLinesRemoved,
                                    DataLinesAdded = @DataLinesAdded, DataLinesRemoved = @DataLinesRemoved,
                                    ConfigLinesAdded = @ConfigLinesAdded, ConfigLinesRemoved = @ConfigLinesRemoved,
                                    DocsLinesAdded = @DocsLinesAdded, DocsLinesRemoved = @DocsLinesRemoved,
                                    IsMerge = @IsMerge, IsPRMergeCommit = @IsPRMergeCommit
                                WHERE Id = @Id;
                            ";
                            await connection.ExecuteAsync(updateSql, new 
                            {
                                Id = existingCommit.Id,
                                LinesAdded = totalAdded,
                                LinesRemoved = totalRemoved,
                                CodeLinesAdded = codeAdded,
                                CodeLinesRemoved = codeRemoved,
                                DataLinesAdded = diffSummary.DataAdded,
                                DataLinesRemoved = diffSummary.DataRemoved,
                                ConfigLinesAdded = diffSummary.ConfigAdded,
                                ConfigLinesRemoved = diffSummary.ConfigRemoved,
                                DocsLinesAdded = diffSummary.DocsAdded,
                                DocsLinesRemoved = diffSummary.DocsRemoved,
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
                            // Use the pre-calculated merge flag and file classification data
                            const string insertSql = @"
                                INSERT INTO Commits (BitbucketCommitHash, RepositoryId, AuthorId, Date, Message, 
                                                   LinesAdded, LinesRemoved, IsMerge, 
                                                   CodeLinesAdded, CodeLinesRemoved,
                                                   DataLinesAdded, DataLinesRemoved,
                                                   ConfigLinesAdded, ConfigLinesRemoved,
                                                   DocsLinesAdded, DocsLinesRemoved,
                                                   IsPRMergeCommit)
                                VALUES (@Hash, @RepoId, @AuthorId, @Date, @Message, 
                                        @LinesAdded, @LinesRemoved, @IsMerge, 
                                        @CodeLinesAdded, @CodeLinesRemoved,
                                        @DataLinesAdded, @DataLinesRemoved,
                                        @ConfigLinesAdded, @ConfigLinesRemoved,
                                        @DocsLinesAdded, @DocsLinesRemoved,
                                        @IsPRMergeCommit);
                            ";
                            
                            var insertedCommitId = await connection.QuerySingleAsync<int>(@"
                                INSERT INTO Commits (BitbucketCommitHash, RepositoryId, AuthorId, Date, Message, 
                                                   LinesAdded, LinesRemoved, IsMerge, 
                                                   CodeLinesAdded, CodeLinesRemoved,
                                                   DataLinesAdded, DataLinesRemoved,
                                                   ConfigLinesAdded, ConfigLinesRemoved,
                                                   DocsLinesAdded, DocsLinesRemoved,
                                                   IsPRMergeCommit)
                                OUTPUT INSERTED.Id
                                VALUES (@Hash, @RepoId, @AuthorId, @Date, @Message, 
                                        @LinesAdded, @LinesRemoved, @IsMerge, 
                                        @CodeLinesAdded, @CodeLinesRemoved,
                                        @DataLinesAdded, @DataLinesRemoved,
                                        @ConfigLinesAdded, @ConfigLinesRemoved,
                                        @DocsLinesAdded, @DocsLinesRemoved,
                                        @IsPRMergeCommit);
                            ", new
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
                                DataLinesAdded = diffSummary.DataAdded,
                                DataLinesRemoved = diffSummary.DataRemoved,
                                ConfigLinesAdded = diffSummary.ConfigAdded,
                                ConfigLinesRemoved = diffSummary.ConfigRemoved,
                                DocsLinesAdded = diffSummary.DocsAdded,
                                DocsLinesRemoved = diffSummary.DocsRemoved,
                                IsPRMergeCommit = isPRMergeCommit
                            });
                            
                            // Insert file-level details
                            await InsertCommitFilesAsync(connection, insertedCommitId, diffSummary.FileChanges);
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

        private async Task InsertCommitFilesAsync(SqlConnection connection, int commitId, List<FileChangeDetail> fileChanges)
        {
            if (!fileChanges.Any()) return;

            const string insertFilesSql = @"
                INSERT INTO CommitFiles (CommitId, FilePath, FileType, ChangeStatus, LinesAdded, LinesRemoved, FileExtension, CreatedOn)
                VALUES (@CommitId, @FilePath, @FileType, @ChangeStatus, @LinesAdded, @LinesRemoved, @FileExtension, @CreatedOn);
            ";

            var fileRecords = fileChanges.Select(fc => new
            {
                CommitId = commitId,
                FilePath = fc.FilePath,
                FileType = fc.FileType.ToString().ToLower(),
                ChangeStatus = fc.ChangeStatus,
                LinesAdded = fc.LinesAdded,
                LinesRemoved = fc.LinesRemoved,
                FileExtension = fc.FileExtension,
                CreatedOn = DateTime.UtcNow
            }).ToArray();

            await connection.ExecuteAsync(insertFilesSql, fileRecords);
            
            _logger.LogDebug("Inserted {FileCount} file change records for commit {CommitId}", 
                fileChanges.Count, commitId);
        }
    }
}
