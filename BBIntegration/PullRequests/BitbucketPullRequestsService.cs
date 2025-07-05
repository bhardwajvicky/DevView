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
                    //_logger.LogInformation("Raw PRs JSON: {Json}", prsJson);
                    var prPagedResponse = JsonSerializer.Deserialize<PaginatedResponseDto<PullRequestDto>>(prsJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                    if (prPagedResponse?.Values == null || !prPagedResponse.Values.Any()) break;

                    foreach (var pr in prPagedResponse.Values)
                    {
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
                    
                    int? finalCommitId = commitId;
                    if (!commitId.HasValue)
                    {
                        // Try to fetch and insert the commit
                        try
                        {
                            // Fetch raw diff and parse it
                            var diffContent = await _apiClient.GetCommitDiffAsync(workspace, repoSlug, commit.Hash);
                            var (totalAdded, totalRemoved, codeAdded, codeRemoved) = _diffParser.ParseDiff(diffContent);

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

                            // Find the repository ID
                            var repoId = await connection.QuerySingleOrDefaultAsync<int?>(
                                "SELECT Id FROM Repositories WHERE Slug = @RepoSlug", new { RepoSlug = repoSlug });
                            if (repoId == null)
                            {
                                _logger.LogWarning("Repository '{RepoSlug}' not found when inserting commit '{CommitHash}'.", repoSlug, commit.Hash);
                                continue;
                            }

                            // Insert the commit
                            bool isMerge = commit.Message != null && commit.Message.Trim().ToLower().StartsWith("merge");
                            bool isPRMergeCommit = false;
                            // If this commit is the merge_commit for the PR, set the flag
                            if (prDbId > 0 && commit.Message != null && commit.Message.Trim().StartsWith("Merge branch"))
                            {
                                isPRMergeCommit = true;
                            }
                            const string insertSql = @"
                                INSERT INTO Commits (BitbucketCommitHash, RepositoryId, AuthorId, Date, Message, LinesAdded, LinesRemoved, IsMerge, CodeLinesAdded, CodeLinesRemoved, IsPRMergeCommit)
                                VALUES (@Hash, @RepoId, @AuthorId, @Date, @Message, @LinesAdded, @LinesRemoved, @IsMerge, @CodeLinesAdded, @CodeLinesRemoved, @IsPRMergeCommit);
                                SELECT SCOPE_IDENTITY();
                            ";
                            var insertedId = await connection.ExecuteScalarAsync<int?>(insertSql, new
                            {
                                commit.Hash,
                                RepoId = repoId.Value,
                                AuthorId = authorId.Value,
                                commit.Date,
                                commit.Message,
                                LinesAdded = totalAdded,
                                LinesRemoved = totalRemoved,
                                IsMerge = isMerge,
                                CodeLinesAdded = codeAdded,
                                CodeLinesRemoved = codeRemoved,
                                IsPRMergeCommit = isPRMergeCommit
                            });
                            finalCommitId = insertedId;
                            _logger.LogInformation("Inserted missing commit '{CommitHash}' for PR '{BitbucketPrId}'.", commit.Hash, bitbucketPrId);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Failed to fetch/insert missing commit '{CommitHash}' for PR '{BitbucketPrId}'.", commit.Hash, bitbucketPrId);
                            continue;
                        }
                    }
                    if (finalCommitId.HasValue)
                    {
                        // Associate commit with the PR
                        const string joinSql = @"
                            IF NOT EXISTS (SELECT 1 FROM PullRequestCommits WHERE PullRequestId = @PrDbId AND CommitId = @CommitId)
                            BEGIN
                                INSERT INTO PullRequestCommits (PullRequestId, CommitId) VALUES (@PrDbId, @CommitId);
                            END
                        ";
                        await connection.ExecuteAsync(joinSql, new { PrDbId = prDbId, CommitId = finalCommitId.Value });
                    }
                }
                commitNextPageUrl = commitPagedResponse.NextPageUrl;
            } while (!string.IsNullOrEmpty(commitNextPageUrl));
        }
    }
}
