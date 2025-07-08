using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Data.SqlClient;
using BBIntegration.Common;
using BBIntegration.Repositories;
using BBIntegration.Commits;
using BBIntegration.PullRequests;
using BBIntegration.Utils;
using Dapper;
using Microsoft.Extensions.Logging;
using System.Collections.Generic; // Added for Dictionary
using System.Linq; // Added for ToDictionary and Min

namespace tVar.AutoSync
{
    class Program
    {
        static async Task Main(string[] args)
        {
            // Load configuration from BB.Api/appsettings.json
            var config = new ConfigurationBuilder()
                .SetBasePath(AppContext.BaseDirectory) // Changed to AppContext.BaseDirectory
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                .Build();

            var connectionString = config.GetConnectionString("DefaultConnection");
            var bitbucketSection = config.GetSection("Bitbucket");
            var bitbucketConfig = bitbucketSection.Get<BitbucketConfig>() ?? new BitbucketConfig();
            bitbucketConfig.DbConnectionString = config.GetConnectionString("DefaultConnection"); // Explicitly set DbConnectionString
            int batchDays = config.GetValue<int?>("AutoSyncBatchDays") ?? 10;

            Console.WriteLine($"Starting tVar.AutoSync with batch size: {batchDays} days");

            // Minimal logger for BBIntegration services
            using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
            var commitLogger = loggerFactory.CreateLogger<BitbucketCommitsService>();
            var prLogger = loggerFactory.CreateLogger<BitbucketPullRequestsService>();
            var diffParserLogger = loggerFactory.CreateLogger<DiffParserService>();
            var fileClassificationLogger = loggerFactory.CreateLogger<FileClassificationService>(); // Added logger for FileClassificationService
            var fileClassificationService = new FileClassificationService(config, fileClassificationLogger); // Passed config and logger
            var diffParser = new DiffParserService(fileClassificationService, diffParserLogger);

            var apiClient = new BitbucketApiClient(bitbucketConfig);
            var repoService = new BitbucketRepositoriesService(bitbucketConfig, apiClient);
            var commitService = new BitbucketCommitsService(bitbucketConfig, apiClient, diffParser, commitLogger);
            var prService = new BitbucketPullRequestsService(apiClient, bitbucketConfig, prLogger, diffParser);

            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync();

            // 1. Get all repositories from DB
            var repos = await connection.QueryAsync<(int Id, string Slug, string Workspace)>(
                "SELECT Id, Slug, Workspace FROM Repositories");

            // Track the current end date for each repository's sync
            var repoCurrentEndDates = repos.ToDictionary(r => r.Slug, r => DateTime.UtcNow.Date);
            var completedRepos = new HashSet<string>(); // New: Keep track of completed repositories

            bool overallMoreHistory = true;

            while (overallMoreHistory)
            {
                overallMoreHistory = false; // Assume no more history until proven otherwise in this iteration
                Console.WriteLine($"\n--- Starting new overall batch. Completed Repos: {string.Join(", ", completedRepos)} ---"); // Corrected debug log

                foreach (var repo in repos)
                {
                    Console.WriteLine($"[DEBUG] Checking repo: {repo.Slug}. Is in completedRepos: {completedRepos.Contains(repo.Slug)}"); // Corrected debug log
                    if (completedRepos.Contains(repo.Slug))
                    {
                        Console.WriteLine($"[DEBUG] Skipping already completed repo: {repo.Slug}"); // Corrected debug log
                        continue; // Skip already completed repositories
                    }

                    DateTime endDate = repoCurrentEndDates[repo.Slug];
                    DateTime startDate = endDate.AddDays(-batchDays);

                    Console.WriteLine($"\n[Repo: {repo.Slug}] Processing from {startDate:yyyy-MM-dd} to {endDate:yyyy-MM-dd}");

                    // Log sync start
                    var logId = await connection.ExecuteScalarAsync<int>(@"
                        INSERT INTO RepositorySyncLog (RepositoryId, StartDate, EndDate, Status, SyncedAt)
                        VALUES (@RepositoryId, @StartDate, @EndDate, @Status, GETUTCDATE());
                        SELECT SCOPE_IDENTITY();",
                        new { RepositoryId = repo.Id, StartDate = startDate, EndDate = endDate, Status = "Started" });

                    try
                    {
                        Console.WriteLine($"[Repo: {repo.Slug}] Syncing commits...");
                        bool hasMoreCommits = await commitService.SyncCommitsAsync(repo.Workspace, repo.Slug, startDate, endDate);
                        Console.WriteLine($"[Repo: {repo.Slug}] Syncing pull requests...");
                        bool hasMorePRs = await prService.SyncPullRequestsAsync(repo.Workspace, repo.Slug, startDate, endDate);
                        Console.WriteLine($"[Repo: {repo.Slug}] Batch complete.");

                        // Log sync complete
                        await connection.ExecuteAsync(@"
                            UPDATE RepositorySyncLog SET Status = @Status, Message = @Message WHERE Id = @Id",
                            new { Id = logId, Status = "Completed", Message = "" });

                        // Update the end date for the next iteration for this specific repo
                        if (hasMoreCommits || hasMorePRs)
                        {
                            repoCurrentEndDates[repo.Slug] = startDate;
                        }
                        else
                        {
                            // If no more history for this repo, add it to completedRepos
                            completedRepos.Add(repo.Slug);
                            Console.WriteLine($"[Repo: {repo.Slug}] No more history found. Marking as complete.");
                        }
                    }
                    catch (Exception ex)
                    {
                        // Log sync failure
                        await connection.ExecuteAsync(@"
                            UPDATE RepositorySyncLog SET Status = @Status, Message = @Message WHERE Id = @Id",
                            new { Id = logId, Status = "Failed", Message = ex.Message });
                        Console.WriteLine($"[Repo: {repo.Slug}] Error: {ex.Message}");
                    }
                }
                // Determine if there's any repository still needing more history
                if (completedRepos.Count < repos.Count())
                {
                    overallMoreHistory = true;
                }
            }

            Console.WriteLine("\ntVar.AutoSync complete.");
        }
    }
}

