using Data.Models;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using Microsoft.Extensions.Configuration;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace API.Endpoints.Analytics
{
    [ApiController]
    [Route("api/[controller]")]
    public class UserDashboardController : ControllerBase
    {
        private readonly string _connectionString;
        private readonly ILogger<UserDashboardController> _logger;

        public UserDashboardController(IConfiguration config, ILogger<UserDashboardController> logger)
        {
            _connectionString = config.GetConnectionString("DefaultConnection") ??
                                throw new InvalidOperationException("DefaultConnection connection string not found.");
            _logger = logger;
        }

        [HttpGet]
        public async Task<IActionResult> GetUserDashboard(
            DateTime? startDate = null,
            DateTime? endDate = null,
            string? repoSlug = null,
            string? workspace = null,
            int? userId = null,
            bool includePR = false,
            bool showExcluded = false)
        {
            // Handle "All Time" when both dates are null - get full date range from data  
            if (!startDate.HasValue && !endDate.HasValue)
            {
                using var tempConnection = new SqlConnection(_connectionString);
                await tempConnection.OpenAsync();
                
                // Get the earliest commit date for true "All Time"
                startDate = await tempConnection.QuerySingleOrDefaultAsync<DateTime?>(
                    "SELECT MIN(Date) FROM Commits c JOIN Repositories r ON c.RepositoryId = r.Id WHERE (@workspace IS NULL OR r.Workspace = @workspace)",
                    new { workspace }) ?? DateTime.Today.AddYears(-10);
                    
                endDate = DateTime.Today;
            }
            // If only one date is null, fill in the missing one  
            else if (!startDate.HasValue || !endDate.HasValue)
            {
                using var tempConnection = new SqlConnection(_connectionString);
                await tempConnection.OpenAsync();
                
                if (!startDate.HasValue)
                {
                    startDate = await tempConnection.QuerySingleOrDefaultAsync<DateTime?>(
                        "SELECT MIN(Date) FROM Commits c JOIN Repositories r ON c.RepositoryId = r.Id WHERE (@workspace IS NULL OR r.Workspace = @workspace)",
                        new { workspace }) ?? DateTime.Today.AddYears(-10);
                }
                
                if (!endDate.HasValue)
                {
                    endDate = DateTime.Today;
                }
            }

            var currentPeriodLength = (endDate.Value - startDate.Value).Days + 1;
            var previousPeriodStartDate = startDate.Value.AddDays(-currentPeriodLength);
            var previousPeriodEndDate = startDate.Value.AddDays(-1);

            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            var currentPeriodStats = await GetPeriodStats(connection, startDate.Value, endDate.Value, repoSlug, workspace, userId, includePR, showExcluded);
            var previousPeriodStats = await GetPeriodStats(connection, previousPeriodStartDate, previousPeriodEndDate, repoSlug, workspace, userId, includePR, showExcluded);
            var prAgeGraphData = await GetPrAgeGraphData(connection, startDate.Value, endDate.Value, repoSlug, workspace);
            var topContributors = await GetTopContributors(connection, startDate.Value, endDate.Value, repoSlug, workspace, userId, includePR, showExcluded);
            var usersWithNoActivity = await GetUsersWithNoActivity(connection, startDate.Value, endDate.Value, repoSlug, workspace, userId, includePR, showExcluded);
            var topApprovers = await GetTopApprovers(connection, startDate.Value, endDate.Value, repoSlug, workspace);
            var prsMergedByWeekdayData = await GetPrsMergedByWeekdayData(connection, startDate.Value, endDate.Value, repoSlug, workspace);

            var response = new UserDashboardResponseDto
            {
                CurrentPeriod = currentPeriodStats,
                PreviousPeriod = previousPeriodStats,
                PrAgeGraphData = prAgeGraphData,
                TopContributors = topContributors,
                UsersWithNoActivity = usersWithNoActivity,
                TopApprovers = topApprovers,
                PrsMergedByWeekdayData = prsMergedByWeekdayData
            };

            return Ok(response);
        }

        private async Task<PeriodStats> GetPeriodStats(SqlConnection connection, DateTime periodStartDate, DateTime periodEndDate, string? repoSlug, string? workspace, int? userId, bool includePR, bool showExcluded)
        {
            // Build WHERE clause similar to Commits API
            var whereConditions = new List<string> { "c.IsRevert = 0" };
            
            if (!string.IsNullOrEmpty(workspace))
                whereConditions.Add("r.Workspace = @workspace");
            if (!string.IsNullOrEmpty(repoSlug) && !string.Equals(repoSlug, "all", StringComparison.OrdinalIgnoreCase))
                whereConditions.Add("r.Slug = @repoSlug");
            if (userId.HasValue)
                whereConditions.Add("c.AuthorId = @userId");
            if (!includePR)
                whereConditions.Add("c.IsPRMergeCommit = 0");
            
            whereConditions.Add("c.Date >= @periodStartDate");
            whereConditions.Add("c.Date <= @periodEndDate");
            
            var whereClause = "WHERE " + string.Join(" AND ", whereConditions);

            // Total Commits
            var totalCommits = await connection.QuerySingleOrDefaultAsync<int>(
                $"SELECT COUNT(*) FROM Commits c JOIN Repositories r ON c.RepositoryId = r.Id {whereClause}", 
                new { periodStartDate, periodEndDate, repoSlug, workspace, userId });

            // Repositories Updated
            var repositoriesUpdated = await connection.QuerySingleOrDefaultAsync<int>(
                $"SELECT COUNT(DISTINCT c.RepositoryId) FROM Commits c JOIN Repositories r ON c.RepositoryId = r.Id {whereClause}", 
                new { periodStartDate, periodEndDate, repoSlug, workspace, userId });

            // Active Contributing Users
            var activeContributingUsers = await connection.QuerySingleOrDefaultAsync<int>($@"
                SELECT COUNT(DISTINCT UserId)
                FROM (
                    -- Commits
                    SELECT c.AuthorId AS UserId FROM Commits c JOIN Repositories r ON c.RepositoryId = r.Id {whereClause}
                    UNION
                    -- PRs Created (if workspace filter is needed, build similar where clause for PRs)
                    SELECT pr.AuthorId AS UserId 
                    FROM PullRequests pr 
                    JOIN Repositories r ON pr.RepositoryId = r.Id
                    WHERE pr.CreatedOn >= @periodStartDate AND pr.CreatedOn <= @periodEndDate
                    {(!string.IsNullOrEmpty(workspace) ? " AND r.Workspace = @workspace" : "")}
                    {(!string.IsNullOrEmpty(repoSlug) && !string.Equals(repoSlug, "all", StringComparison.OrdinalIgnoreCase) ? " AND r.Slug = @repoSlug" : "")}
                    UNION
                    -- PRs Approved
                    SELECT u.Id AS UserId
                    FROM PullRequestApprovals pra
                    JOIN PullRequests pr ON pra.PullRequestId = pr.Id
                    JOIN Users u ON pra.UserUuid = u.BitbucketUserId
                    JOIN Repositories r ON pr.RepositoryId = r.Id
                    WHERE pra.ApprovedOn >= @periodStartDate AND pra.ApprovedOn <= @periodEndDate AND pra.Approved = 1
                    {(!string.IsNullOrEmpty(workspace) ? " AND r.Workspace = @workspace" : "")}
                    {(!string.IsNullOrEmpty(repoSlug) && !string.Equals(repoSlug, "all", StringComparison.OrdinalIgnoreCase) ? " AND r.Slug = @repoSlug" : "")}
                ) AS UserActivity
            ", new { periodStartDate, periodEndDate, repoSlug, workspace, userId });

            // Total Licensed Users - This needs to be determined. For now, let's assume it's the total number of users in the system.
            // This query does not depend on repoSlug
            var totalLicensedUsers = await connection.QuerySingleOrDefaultAsync<int>(
                "SELECT COUNT(*) FROM Users WHERE CreatedOn <= @periodEndDate", new { periodEndDate });

            // PRs Not Approved and Merged - Build PR where clause
            var prWhereConditions = new List<string>();
            if (!string.IsNullOrEmpty(workspace))
                prWhereConditions.Add("r.Workspace = @workspace");
            if (!string.IsNullOrEmpty(repoSlug) && !string.Equals(repoSlug, "all", StringComparison.OrdinalIgnoreCase))
                prWhereConditions.Add("r.Slug = @repoSlug");
            
            prWhereConditions.Add("pr.CreatedOn >= @periodStartDate");
            prWhereConditions.Add("pr.CreatedOn <= @periodEndDate");
            prWhereConditions.Add("pr.State != 'MERGED'");
            prWhereConditions.Add("pr.State != 'DECLINED'");
            
            var prWhereClause = "WHERE " + string.Join(" AND ", prWhereConditions);
            
            var prsNotApprovedAndMerged = await connection.QuerySingleOrDefaultAsync<int>(
                $"SELECT COUNT(*) FROM PullRequests pr JOIN Repositories r ON pr.RepositoryId = r.Id {prWhereClause}", 
                new { periodStartDate, periodEndDate, repoSlug, workspace });

            // Total Merged PRs
            var mergedPrWhereConditions = new List<string>();
            if (!string.IsNullOrEmpty(workspace))
                mergedPrWhereConditions.Add("r.Workspace = @workspace");
            if (!string.IsNullOrEmpty(repoSlug) && !string.Equals(repoSlug, "all", StringComparison.OrdinalIgnoreCase))
                mergedPrWhereConditions.Add("r.Slug = @repoSlug");
            
            mergedPrWhereConditions.Add("pr.MergedOn >= @periodStartDate");
            mergedPrWhereConditions.Add("pr.MergedOn <= @periodEndDate");
            mergedPrWhereConditions.Add("pr.State = 'MERGED'");
            
            var mergedPrWhereClause = "WHERE " + string.Join(" AND ", mergedPrWhereConditions);
            
            var totalMergedPrs = await connection.QuerySingleOrDefaultAsync<int>(
                $"SELECT COUNT(*) FROM PullRequests pr JOIN Repositories r ON pr.RepositoryId = r.Id {mergedPrWhereClause}",
                new { periodStartDate, periodEndDate, repoSlug, workspace });

            return new PeriodStats
            {
                StartDate = periodStartDate,
                EndDate = periodEndDate,
                TotalCommits = totalCommits,
                RepositoriesUpdated = repositoriesUpdated,
                ActiveContributingUsers = activeContributingUsers,
                TotalLicensedUsers = totalLicensedUsers, 
                PrsNotApprovedAndMerged = prsNotApprovedAndMerged,
                TotalMergedPrs = totalMergedPrs
            };
        }

        private async Task<PrAgeGraph> GetPrAgeGraphData(SqlConnection connection, DateTime periodStartDate, DateTime periodEndDate, string? repoSlug, string? workspace)
        {
            var openPrAgeData = new List<PrAgeDataPoint>();
            var mergedPrAgeData = new List<PrAgeDataPoint>();

            var repoWhereClause = string.IsNullOrEmpty(repoSlug) ? "" : " AND RepositoryId = (SELECT Id FROM Repositories WHERE Slug = @repoSlug)";

            // Open PRs Age
            _logger.LogInformation("Fetching open PRs from {PeriodStartDate} to {PeriodEndDate} for repo {RepoSlug}", periodStartDate, periodEndDate, repoSlug);
            var openPrs = await connection.QueryAsync<DateTime>($@"
                SELECT pr.CreatedOn 
                FROM PullRequests pr 
                JOIN Repositories r ON pr.RepositoryId = r.Id
                WHERE pr.CreatedOn >= CAST(@periodStartDate AS DATETIME2) AND pr.CreatedOn <= CAST(@periodEndDate AS DATETIME2) AND pr.State = 'OPEN'
                {(!string.IsNullOrEmpty(workspace) ? " AND r.Workspace = @workspace" : "")}
                {(!string.IsNullOrEmpty(repoSlug) && !string.Equals(repoSlug, "all", StringComparison.OrdinalIgnoreCase) ? " AND r.Slug = @repoSlug" : "")}",
                new { periodStartDate, periodEndDate, repoSlug, workspace });

            _logger.LogInformation("Found {Count} open PRs", openPrs.Count());

            foreach (var createdOn in openPrs)
            {
                var ageInDays = (int)(DateTime.Today - createdOn).TotalDays;
                var existingPoint = openPrAgeData.FirstOrDefault(p => p.Days == ageInDays);
                if (existingPoint == null)
                {
                    openPrAgeData.Add(new PrAgeDataPoint { Days = ageInDays, PrCount = 1 });
                }
                else
                {
                    existingPoint.PrCount++;
                }
            }
            openPrAgeData = openPrAgeData.OrderBy(p => p.Days).ToList();

            // Merged PRs Age
            _logger.LogInformation("Fetching merged PRs from {PeriodStartDate} to {PeriodEndDate} for repo {RepoSlug}", periodStartDate, periodEndDate, repoSlug);
            var mergedPrs = await connection.QueryAsync<(DateTime CreatedOn, DateTime MergedOn)>($@"
                SELECT pr.CreatedOn, pr.MergedOn 
                FROM PullRequests pr 
                JOIN Repositories r ON pr.RepositoryId = r.Id
                WHERE pr.MergedOn >= CAST(@periodStartDate AS DATETIME2) AND pr.MergedOn <= CAST(@periodEndDate AS DATETIME2) AND pr.State = 'MERGED'
                {(!string.IsNullOrEmpty(workspace) ? " AND r.Workspace = @workspace" : "")}
                {(!string.IsNullOrEmpty(repoSlug) && !string.Equals(repoSlug, "all", StringComparison.OrdinalIgnoreCase) ? " AND r.Slug = @repoSlug" : "")}",
                new { periodStartDate, periodEndDate, repoSlug, workspace });
            
            _logger.LogInformation("Found {Count} merged PRs", mergedPrs.Count());

            foreach (var pr in mergedPrs)
            {
                var ageInDays = (int)(pr.MergedOn - pr.CreatedOn).TotalDays;
                var existingPoint = mergedPrAgeData.FirstOrDefault(p => p.Days == ageInDays);
                if (existingPoint == null)
                {
                    mergedPrAgeData.Add(new PrAgeDataPoint { Days = ageInDays, PrCount = 1 });
                }
                else
                {
                    existingPoint.PrCount++;
                }
            }
            mergedPrAgeData = mergedPrAgeData.OrderBy(p => p.Days).ToList();

            return new PrAgeGraph
            {
                OpenPrAge = openPrAgeData,
                MergedPrAge = mergedPrAgeData
            };
        }

        private async Task<List<ContributorStats>> GetTopContributors(SqlConnection connection, DateTime periodStartDate, DateTime periodEndDate, string? repoSlug, string? workspace, int? userId, bool includePR, bool showExcluded)
        {
            // Build WHERE clause similar to Commits API
            var whereConditions = new List<string> { "c.IsRevert = 0" };
            
            if (!string.IsNullOrEmpty(workspace))
                whereConditions.Add("r.Workspace = @workspace");
            if (!string.IsNullOrEmpty(repoSlug) && !string.Equals(repoSlug, "all", StringComparison.OrdinalIgnoreCase))
                whereConditions.Add("r.Slug = @repoSlug");
            if (userId.HasValue)
                whereConditions.Add("c.AuthorId = @userId");
            if (!includePR)
                whereConditions.Add("c.IsPRMergeCommit = 0");
            
            whereConditions.Add("c.Date >= @periodStartDate");
            whereConditions.Add("c.Date <= @periodEndDate");
            
            var whereClause = "WHERE " + string.Join(" AND ", whereConditions);
            
            var topContributors = await connection.QueryAsync<ContributorStats>($@"
                SELECT u.DisplayName AS UserName, COUNT(c.Id) AS Commits,
                       ISNULL(SUM(cf.LinesAdded), 0) AS CodeLinesAdded,
                       ISNULL(SUM(cf.LinesRemoved), 0) AS CodeLinesRemoved
                FROM Commits c
                JOIN Users u ON c.AuthorId = u.Id
                JOIN Repositories r ON c.RepositoryId = r.Id
                LEFT JOIN CommitFiles cf ON c.Id = cf.CommitId AND cf.FileType = 'code' AND cf.ExcludeFromReporting = 0
                {whereClause}
                GROUP BY u.DisplayName
                ORDER BY Commits DESC, CodeLinesAdded DESC
            ", new { periodStartDate, periodEndDate, repoSlug, workspace, userId });

            return topContributors.ToList();
        }

        private async Task<int> GetUsersWithNoActivity(SqlConnection connection, DateTime periodStartDate, DateTime periodEndDate, string? repoSlug, string? workspace, int? userId, bool includePR, bool showExcluded)
        {
            // Get the count of distinct users who had activity in the period
            var activeUserCount = await connection.QuerySingleAsync<int>($@"
                SELECT COUNT(DISTINCT UserId)
                FROM (
                    -- Commits
                    SELECT c.AuthorId AS UserId 
                    FROM Commits c 
                    JOIN Repositories r ON c.RepositoryId = r.Id
                    WHERE c.Date >= @periodStartDate AND c.Date <= @periodEndDate AND c.IsRevert = 0
                    {(!string.IsNullOrEmpty(workspace) ? " AND r.Workspace = @workspace" : "")}
                    {(!string.IsNullOrEmpty(repoSlug) && !string.Equals(repoSlug, "all", StringComparison.OrdinalIgnoreCase) ? " AND r.Slug = @repoSlug" : "")}
                    UNION
                    -- PRs Created
                    SELECT pr.AuthorId AS UserId 
                    FROM PullRequests pr 
                    JOIN Repositories r ON pr.RepositoryId = r.Id
                    WHERE pr.CreatedOn >= @periodStartDate AND pr.CreatedOn <= @periodEndDate
                    {(!string.IsNullOrEmpty(workspace) ? " AND r.Workspace = @workspace" : "")}
                    {(!string.IsNullOrEmpty(repoSlug) && !string.Equals(repoSlug, "all", StringComparison.OrdinalIgnoreCase) ? " AND r.Slug = @repoSlug" : "")}
                    UNION
                    -- PRs Approved
                    SELECT u.Id AS UserId
                    FROM PullRequestApprovals pra
                    JOIN PullRequests pr ON pra.PullRequestId = pr.Id
                    JOIN Users u ON pra.UserUuid = u.BitbucketUserId
                    JOIN Repositories r ON pr.RepositoryId = r.Id
                    WHERE pra.ApprovedOn >= @periodStartDate AND pra.ApprovedOn <= @periodEndDate AND pra.Approved = 1
                    {(!string.IsNullOrEmpty(workspace) ? " AND r.Workspace = @workspace" : "")}
                    {(!string.IsNullOrEmpty(repoSlug) && !string.Equals(repoSlug, "all", StringComparison.OrdinalIgnoreCase) ? " AND r.Slug = @repoSlug" : "")}
                ) AS UserActivity
            ", new { periodStartDate, periodEndDate, repoSlug, workspace });

            // Get the total number of users
            var totalUserCount = await connection.QuerySingleAsync<int>("SELECT COUNT(*) FROM Users");

            // Inactive users = Total users - Active users
            return totalUserCount - activeUserCount;
        }

        private async Task<List<ApproverStats>> GetTopApprovers(SqlConnection connection, DateTime periodStartDate, DateTime periodEndDate, string? repoSlug, string? workspace)
        {
            var repoWhereClause = string.IsNullOrEmpty(repoSlug) ? "" : " AND pr.RepositoryId = (SELECT Id FROM Repositories WHERE Slug = @repoSlug)";
            var topApprovers = await connection.QueryAsync<ApproverStats>($@"
                SELECT u.DisplayName AS UserName, COUNT(pra.Id) AS PrApprovalCount
                FROM PullRequestApprovals pra
                JOIN PullRequests pr ON pra.PullRequestId = pr.Id
                JOIN Users u ON pra.UserUuid = u.BitbucketUserId
                JOIN Repositories r ON pr.RepositoryId = r.Id
                WHERE pr.CreatedOn >= @periodStartDate AND pr.CreatedOn <= @periodEndDate
                {(!string.IsNullOrEmpty(workspace) ? " AND r.Workspace = @workspace" : "")}
                {(!string.IsNullOrEmpty(repoSlug) && !string.Equals(repoSlug, "all", StringComparison.OrdinalIgnoreCase) ? " AND r.Slug = @repoSlug" : "")}
                GROUP BY u.DisplayName
                ORDER BY PrApprovalCount DESC
            ", new { periodStartDate, periodEndDate, repoSlug, workspace });

            return topApprovers.ToList();
        }

        private async Task<PrsMergedByWeekdayData> GetPrsMergedByWeekdayData(SqlConnection connection, DateTime periodStartDate, DateTime periodEndDate, string? repoSlug, string? workspace)
        {
            var repoWhereClause = string.IsNullOrEmpty(repoSlug) ? "" : " AND RepositoryId = (SELECT Id FROM Repositories WHERE Slug = @repoSlug)";
            var query = $@"
                SELECT DATENAME(dw, MergedOn) AS DayOfWeek, COUNT(*) AS PrCount
                FROM PullRequests pr
                JOIN Repositories r ON pr.RepositoryId = r.Id
                WHERE pr.MergedOn >= CAST(@periodStartDate AS DATETIME2) AND pr.MergedOn <= CAST(@periodEndDate AS DATETIME2) AND pr.State = 'MERGED'
                {(!string.IsNullOrEmpty(workspace) ? " AND r.Workspace = @workspace" : "")}
                {(!string.IsNullOrEmpty(repoSlug) && !string.Equals(repoSlug, "all", StringComparison.OrdinalIgnoreCase) ? " AND r.Slug = @repoSlug" : "")}
                GROUP BY DATENAME(dw, MergedOn), DATEPART(dw, MergedOn)
                ORDER BY DATEPART(dw, MergedOn);
            ";

            var mergedPrs = await connection.QueryAsync<WeekdayPrCount>(query, new { periodStartDate, periodEndDate, repoSlug, workspace });

            return new PrsMergedByWeekdayData
            {
                MergedPrsByWeekday = mergedPrs.ToList()
            };
        }
    }
} 