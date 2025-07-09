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
using tVar.AutoSync.Models; // Added this line
using BBIntegration.Users; // Added this line
using BBIntegration.Repositories; // Added this line

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
            var syncSettings = config.GetSection("SyncSettings").Get<SyncSettings>() ?? new SyncSettings(); // Load SyncSettings

            Console.WriteLine($"Starting tVar.AutoSync with batch size: {batchDays} days");
            Console.WriteLine($"Sync Mode: {syncSettings.Mode}, Overwrite: {syncSettings.Overwrite}");

            // Minimal logger for BBIntegration services
            using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
            var commitLogger = loggerFactory.CreateLogger<BitbucketCommitsService>();
            var prLogger = loggerFactory.CreateLogger<BitbucketPullRequestsService>();
            var diffParserLogger = loggerFactory.CreateLogger<DiffParserService>();
            var fileClassificationLogger = loggerFactory.CreateLogger<FileClassificationService>(); // Added logger for FileClassificationService
            var userLogger = loggerFactory.CreateLogger<BitbucketUsersService>(); // Added logger for BitbucketUsersService

            var fileClassificationService = new FileClassificationService(config, fileClassificationLogger); // Passed config and logger
            var diffParser = new DiffParserService(fileClassificationService, diffParserLogger);

            var apiClient = new BitbucketApiClient(bitbucketConfig);
            var repoService = new BitbucketRepositoriesService(bitbucketConfig, apiClient);
            var commitService = new BitbucketCommitsService(bitbucketConfig, apiClient, diffParser, commitLogger);
            var prService = new BitbucketPullRequestsService(apiClient, bitbucketConfig, prLogger, diffParser);
            var userService = new BitbucketUsersService(bitbucketConfig, apiClient, userLogger); // Instantiated BitbucketUsersService

            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync();

            // 1. Get all repositories from DB (moved here to be available for user/repo sync checks)
            var repos = await connection.QueryAsync<(int Id, string Slug, string Workspace)>(
                "SELECT Id, Slug, Workspace FROM Repositories");

            // Optional: Sync Users and Repositories first if enabled
            if (syncSettings.SyncTargets.Users)
            {
                Console.WriteLine("\nSyncing users...");
                var workspaces = repos.Select(r => r.Workspace).Distinct(); // Get distinct workspaces
                foreach (var workspace in workspaces)
                {
                    Console.WriteLine($"Syncing users for workspace: {workspace}");
                    await userService.SyncUsersAsync(workspace);
                }
            }

            if (syncSettings.SyncTargets.Repositories)
            {
                Console.WriteLine("Syncing repositories...");
                var workspaces = repos.Select(r => r.Workspace).Distinct(); // Get distinct workspaces
                foreach (var workspace in workspaces)
                {
                    Console.WriteLine($"Syncing repositories for workspace: {workspace}");
                    await repoService.SyncRepositoriesAsync(workspace);
                }
            }

            if (!repos.Any())
            {
                Console.WriteLine("No repositories found in the database. Please ensure repositories are synced first.");
                Console.WriteLine("tVar.AutoSync complete.");
                return;
            }

            // Track the current end date for each repository's sync
            var repoCurrentEndDates = repos.ToDictionary(r => r.Slug, r => DateTime.UtcNow.Date);
            var completedRepos = new HashSet<string>(); // Keep track of completed repositories for Full sync mode

            bool overallMoreHistory = true;

            if (syncSettings.Mode == "Delta")
            {
                Console.WriteLine($"Running in DELTA mode: syncing last {syncSettings.DeltaSyncDays} days.");
                overallMoreHistory = false; // Delta mode runs only once

                foreach (var repo in repos)
                {
                    if (!syncSettings.SyncTargets.Commits && !syncSettings.SyncTargets.PullRequests) continue;

                    DateTime endDate = DateTime.UtcNow.Date;
                    DateTime startDate = endDate.AddDays(-syncSettings.DeltaSyncDays);

                    Console.WriteLine($"\n[Repo: {repo.Slug}] Processing DELTA from {startDate:yyyy-MM-dd} to {endDate:yyyy-MM-dd}");

                    // Log sync start
                    var logId = await connection.ExecuteScalarAsync<int>(@"
                        INSERT INTO RepositorySyncLog (RepositoryId, StartDate, EndDate, Status, SyncedAt)
                        VALUES (@RepositoryId, @StartDate, @EndDate, @Status, GETUTCDATE());
                        SELECT SCOPE_IDENTITY();",
                        new { RepositoryId = repo.Id, StartDate = startDate, EndDate = endDate, Status = "Started" });

                    try
                    {
                        if (syncSettings.SyncTargets.Commits)
                        {
                            Console.WriteLine($"[Repo: {repo.Slug}] Syncing commits...");
                            await commitService.SyncCommitsAsync(repo.Workspace, repo.Slug, startDate, endDate);
                        }
                        if (syncSettings.SyncTargets.PullRequests)
                        {
                            Console.WriteLine($"[Repo: {repo.Slug}] Syncing pull requests...");
                            await prService.SyncPullRequestsAsync(repo.Workspace, repo.Slug, startDate, endDate);
                        }
                        Console.WriteLine($"[Repo: {repo.Slug}] DELTA Batch complete.");

                        // Log sync complete
                        await connection.ExecuteAsync(@"
                            UPDATE RepositorySyncLog SET Status = @Status, Message = @Message WHERE Id = @Id",
                            new { Id = logId, Status = "Completed", Message = "" });
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
            }
            else // Full Sync Mode
            {
                Console.WriteLine("Running in FULL mode: syncing all history in 10-day blocks.");

                while (overallMoreHistory)
                {
                    overallMoreHistory = false; // Assume no more history until proven otherwise in this iteration
                    Console.WriteLine($"\n--- Starting new overall batch. Completed Repos: {string.Join(", ", completedRepos)} ---");

                    foreach (var repo in repos)
                    {
                        Console.WriteLine($"[DEBUG] Checking repo: {repo.Slug}. Is in completedRepos: {completedRepos.Contains(repo.Slug)}");
                        if (completedRepos.Contains(repo.Slug))
                        {
                            Console.WriteLine($"[DEBUG] Skipping already completed repo: {repo.Slug}");
                            continue; // Skip already completed repositories
                        }

                        DateTime endDate = repoCurrentEndDates[repo.Slug];
                        DateTime startDate = endDate.AddDays(-batchDays);

                        // Check if this date range has already been successfully synced if Overwrite is false
                        bool alreadySynced = false;
                        if (!syncSettings.Overwrite)
                        {
                            var completedLog = await connection.QuerySingleOrDefaultAsync<int?>(
                                "SELECT Id FROM RepositorySyncLog WHERE RepositoryId = @RepositoryId AND StartDate = @StartDate AND EndDate = @EndDate AND Status = 'Completed'",
                                new { RepositoryId = repo.Id, StartDate = startDate, EndDate = endDate });
                            if (completedLog != null)
                            {
                                alreadySynced = true;
                                Console.WriteLine($"[Repo: {repo.Slug}] Skipping already synced batch from {startDate:yyyy-MM-dd} to {endDate:yyyy-MM-dd} (Overwrite is false).");
                            }
                        }
                        
                        if (alreadySynced) {
                            // If already synced, and not overwriting, we mark this repo's current end date as this start date
                            // to ensure it moves to the next older batch in the next overall iteration.
                            repoCurrentEndDates[repo.Slug] = startDate;
                            overallMoreHistory = true; // Still more history to check for other repos or older dates of this repo
                            continue; 
                        }

                        Console.WriteLine($"\n[Repo: {repo.Slug}] Processing from {startDate:yyyy-MM-dd} to {endDate:yyyy-MM-dd}");

                        // Log sync start
                        var logId = await connection.ExecuteScalarAsync<int>(@"
                            INSERT INTO RepositorySyncLog (RepositoryId, StartDate, EndDate, Status, SyncedAt)
                            VALUES (@RepositoryId, @StartDate, @EndDate, @Status, GETUTCDATE());
                            SELECT SCOPE_IDENTITY();",
                            new { RepositoryId = repo.Id, StartDate = startDate, EndDate = endDate, Status = "Started" });

                        try
                        {
                            bool hasMoreCommits = false;
                            bool hasMorePRs = false;

                            if (syncSettings.SyncTargets.Commits)
                            {
                                Console.WriteLine($"[Repo: {repo.Slug}] Syncing commits...");
                                hasMoreCommits = await commitService.SyncCommitsAsync(repo.Workspace, repo.Slug, startDate, endDate);
                            }
                            if (syncSettings.SyncTargets.PullRequests)
                            {
                                Console.WriteLine($"[Repo: {repo.Slug}] Syncing pull requests...");
                                hasMorePRs = await prService.SyncPullRequestsAsync(repo.Workspace, repo.Slug, startDate, endDate);
                            }
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
            }

            Console.WriteLine("\ntVar.AutoSync complete.");
        }
    }
}

