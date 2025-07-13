using Integration.Common;
using Integration.Utils;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace API.Services
{
    public class CommitRefreshService
    {
        private readonly string _connectionString;
        private readonly DiffParserService _diffParser;
        private readonly BitbucketApiClient _apiClient;
        private readonly ILogger<CommitRefreshService> _logger;

        public CommitRefreshService(BitbucketConfig config, DiffParserService diffParser, BitbucketApiClient apiClient, ILogger<CommitRefreshService> logger)
        {
            _connectionString = config.DbConnectionString;
            _diffParser = diffParser;
            _apiClient = apiClient;
            _logger = logger;
        }

        public async Task<int> RefreshAllCommitLineCountsAsync()
        {
            _logger.LogInformation("Starting refresh of all commit line counts.");
            int updatedCount = 0;

            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            // Get all existing commits that need line classification (i.e., CodeLinesAdded is null or 0, or just re-process all)
            // For now, let's re-process all commits to ensure correctness.
            var commitsToRefresh = await connection.QueryAsync<CommitRecord>("SELECT Id, RepositoryId, BitbucketCommitHash, Message, AuthorId, Date FROM Commits");
            
            foreach (var commit in commitsToRefresh)
            {
                try
                {
                    // Retrieve repository slug and workspace for fetching diff
                    var repoInfo = await connection.QuerySingleOrDefaultAsync<RepositoryInfo>(
                        "SELECT r.Slug, r.Workspace FROM Repositories r WHERE r.Id = @RepositoryId", 
                        new { commit.RepositoryId });

                    if (repoInfo == null)
                    {
                        _logger.LogWarning("Repository not found for commit {CommitId}. Skipping.", commit.Id);
                        continue;
                    }

                    // Fetch the raw diff content from Bitbucket
                    var diffContent = await _apiClient.GetCommitDiffAsync(repoInfo.Workspace, repoInfo.Slug, commit.BitbucketCommitHash);
                    var diffSummary = _diffParser.ParseDiffWithClassification(diffContent);

                    // Update the commit in the database with new line counts
                    var updateSql = @"
                        UPDATE Commits
                        SET 
                            LinesAdded = @TotalAdded,
                            LinesRemoved = @TotalRemoved,
                            CodeLinesAdded = @CodeAdded,
                            CodeLinesRemoved = @CodeRemoved,
                            DataLinesAdded = @DataAdded,
                            DataLinesRemoved = @DataRemoved,
                            ConfigLinesAdded = @ConfigAdded,
                            ConfigLinesRemoved = @ConfigRemoved,
                            DocsLinesAdded = @DocsAdded,
                            DocsLinesRemoved = @DocsRemoved
                        WHERE Id = @Id;";

                    var parameters = new
                    {
                        commit.Id,
                        diffSummary.TotalAdded,
                        diffSummary.TotalRemoved,
                        diffSummary.CodeAdded,
                        diffSummary.CodeRemoved,
                        diffSummary.DataAdded,
                        diffSummary.DataRemoved,
                        diffSummary.ConfigAdded,
                        diffSummary.ConfigRemoved,
                        diffSummary.DocsAdded,
                        diffSummary.DocsRemoved
                    };

                    await connection.ExecuteAsync(updateSql, parameters);
                    updatedCount++;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error refreshing commit {CommitHash}: {Message}", commit.BitbucketCommitHash, ex.Message);
                }
            }

            _logger.LogInformation("Finished refreshing commit line counts. Total commits updated: {UpdatedCount}", updatedCount);
            return updatedCount;
        }

        private class CommitRecord
        {
            public int Id { get; set; }
            public int RepositoryId { get; set; }
            public string BitbucketCommitHash { get; set; } = string.Empty;
            public string Message { get; set; } = string.Empty;
            public int AuthorId { get; set; }
            public DateTime Date { get; set; }
        }

        private class RepositoryInfo
        {
            public string Slug { get; set; } = string.Empty;
            public string Workspace { get; set; } = string.Empty;
        }
    }
} 