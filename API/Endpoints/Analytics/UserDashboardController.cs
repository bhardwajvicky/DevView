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
            string? repoSlug = null)
        {
            if (!startDate.HasValue || !endDate.HasValue)
            {
                // Default to last 30 days if no date range is provided
                endDate = DateTime.Today;
                startDate = DateTime.Today.AddDays(-29);
            }

            var currentPeriodLength = (endDate.Value - startDate.Value).Days + 1;
            var previousPeriodStartDate = startDate.Value.AddDays(-currentPeriodLength);
            var previousPeriodEndDate = startDate.Value.AddDays(-1);

            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            var currentPeriodStats = await GetPeriodStats(connection, startDate.Value, endDate.Value, repoSlug);
            var previousPeriodStats = await GetPeriodStats(connection, previousPeriodStartDate, previousPeriodEndDate, repoSlug);
            var prAgeGraphData = await GetPrAgeGraphData(connection, startDate.Value, endDate.Value, repoSlug);
            var topContributors = await GetTopContributors(connection, startDate.Value, endDate.Value, repoSlug);
            var usersWithNoActivity = await GetUsersWithNoActivity(connection, startDate.Value, endDate.Value, repoSlug);
            var topApprovers = await GetTopApprovers(connection, startDate.Value, endDate.Value, repoSlug);
            var prsMergedByWeekdayData = await GetPrsMergedByWeekdayData(connection, startDate.Value, endDate.Value, repoSlug);

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

        private async Task<PeriodStats> GetPeriodStats(SqlConnection connection, DateTime periodStartDate, DateTime periodEndDate, string? repoSlug)
        {
            var repoWhereClause = string.IsNullOrEmpty(repoSlug) ? "" : " AND RepositoryId = (SELECT Id FROM Repositories WHERE Slug = @repoSlug)";

            // Total Commits
            var totalCommits = await connection.QuerySingleOrDefaultAsync<int>(
                $"SELECT COUNT(*) FROM Commits WHERE Date >= @periodStartDate AND Date <= @periodEndDate AND IsRevert = 0{repoWhereClause}", 
                new { periodStartDate, periodEndDate, repoSlug });

            // Repositories Updated
            var repositoriesUpdated = await connection.QuerySingleOrDefaultAsync<int>(
                $"SELECT COUNT(DISTINCT RepositoryId) FROM Commits WHERE Date >= @periodStartDate AND Date <= @periodEndDate AND IsRevert = 0{repoWhereClause}", 
                new { periodStartDate, periodEndDate, repoSlug });

            // Active Contributing Users
            var activeContributingUsers = await connection.QuerySingleOrDefaultAsync<int>(@$"
                SELECT COUNT(DISTINCT UserId)
                FROM (
                    -- Commits
                    SELECT AuthorId AS UserId FROM Commits WHERE Date >= @periodStartDate AND Date <= @periodEndDate AND IsRevert = 0{repoWhereClause.Replace("AND RepositoryId", "AND Commits.RepositoryId")}
                    UNION
                    -- PRs Created
                    SELECT AuthorId AS UserId FROM PullRequests WHERE CreatedOn >= @periodStartDate AND CreatedOn <= @periodEndDate{repoWhereClause.Replace("AND RepositoryId", "AND PullRequests.RepositoryId")}
                    UNION
                    -- PRs Approved
                    SELECT u.Id AS UserId
                    FROM PullRequestApprovals pra
                    JOIN PullRequests pr ON pra.PullRequestId = pr.Id
                    JOIN Users u ON pra.UserUuid = u.BitbucketUserId
                    WHERE pra.ApprovedOn >= @periodStartDate AND pra.ApprovedOn <= @periodEndDate AND pra.Approved = 1{repoWhereClause.Replace("AND RepositoryId", "AND pr.RepositoryId")}
                ) AS UserActivity
            ", new { periodStartDate, periodEndDate, repoSlug });

            // Total Licensed Users - This needs to be determined. For now, let's assume it's the total number of users in the system.
            // This query does not depend on repoSlug
            var totalLicensedUsers = await connection.QuerySingleOrDefaultAsync<int>(
                "SELECT COUNT(*) FROM Users WHERE CreatedOn <= @periodEndDate", new { periodEndDate });

            // PRs Not Approved and Merged
            var prsNotApprovedAndMerged = await connection.QuerySingleOrDefaultAsync<int>(
                $"SELECT COUNT(*) FROM PullRequests WHERE CreatedOn >= @periodStartDate AND CreatedOn <= @periodEndDate AND State != 'MERGED' AND State != 'DECLINED'{repoWhereClause}", 
                new { periodStartDate, periodEndDate, repoSlug });

            // Total Merged PRs
            var totalMergedPrs = await connection.QuerySingleOrDefaultAsync<int>(
                $"SELECT COUNT(*) FROM PullRequests WHERE MergedOn >= @periodStartDate AND MergedOn <= @periodEndDate AND State = 'MERGED'{repoWhereClause}",
                new { periodStartDate, periodEndDate, repoSlug });

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

        private async Task<PrAgeGraph> GetPrAgeGraphData(SqlConnection connection, DateTime periodStartDate, DateTime periodEndDate, string? repoSlug)
        {
            var openPrAgeData = new List<PrAgeDataPoint>();
            var mergedPrAgeData = new List<PrAgeDataPoint>();

            var repoWhereClause = string.IsNullOrEmpty(repoSlug) ? "" : " AND RepositoryId = (SELECT Id FROM Repositories WHERE Slug = @repoSlug)";

            // Open PRs Age
            _logger.LogInformation("Fetching open PRs from {PeriodStartDate} to {PeriodEndDate} for repo {RepoSlug}", periodStartDate, periodEndDate, repoSlug);
            var openPrs = await connection.QueryAsync<DateTime>(
                $"SELECT CreatedOn FROM PullRequests WHERE CreatedOn >= CAST(@periodStartDate AS DATETIME2) AND CreatedOn <= CAST(@periodEndDate AS DATETIME2) AND State = 'OPEN'{repoWhereClause}",
                new { periodStartDate, periodEndDate, repoSlug });

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
            var mergedPrs = await connection.QueryAsync<(DateTime CreatedOn, DateTime MergedOn)>(
                $"SELECT CreatedOn, MergedOn FROM PullRequests WHERE MergedOn >= CAST(@periodStartDate AS DATETIME2) AND MergedOn <= CAST(@periodEndDate AS DATETIME2) AND State = 'MERGED'{repoWhereClause}",
                new { periodStartDate, periodEndDate, repoSlug });
            
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

        private async Task<List<ContributorStats>> GetTopContributors(SqlConnection connection, DateTime periodStartDate, DateTime periodEndDate, string? repoSlug)
        {
            var repoWhereClause = string.IsNullOrEmpty(repoSlug) ? "" : " AND c.RepositoryId = (SELECT Id FROM Repositories WHERE Slug = @repoSlug)";
            var topContributors = await connection.QueryAsync<ContributorStats>(@$"
                SELECT TOP 5 u.DisplayName AS UserName, COUNT(c.Id) AS Commits,
                       ISNULL(SUM(cf.LinesAdded), 0) AS CodeLinesAdded,
                       ISNULL(SUM(cf.LinesRemoved), 0) AS CodeLinesRemoved
                FROM Commits c
                JOIN Users u ON c.AuthorId = u.Id
                LEFT JOIN CommitFiles cf ON c.Id = cf.CommitId AND cf.FileType = 'code' AND cf.ExcludeFromReporting = 0
                WHERE c.Date >= @periodStartDate AND c.Date <= @periodEndDate AND c.IsRevert = 0{repoWhereClause}
                GROUP BY u.DisplayName
                ORDER BY Commits DESC, CodeLinesAdded DESC
            ", new { periodStartDate, periodEndDate, repoSlug });

            return topContributors.ToList();
        }

        private async Task<int> GetUsersWithNoActivity(SqlConnection connection, DateTime periodStartDate, DateTime periodEndDate, string? repoSlug)
        {
            var repoWhereClause = string.IsNullOrEmpty(repoSlug) ? "" : " AND c.RepositoryId = (SELECT Id FROM Repositories WHERE Slug = @repoSlug)";
            
            // Get the count of distinct users who had activity in the period
            var activeUserCount = await connection.QuerySingleAsync<int>(@$"
                SELECT COUNT(DISTINCT UserId)
                FROM (
                    -- Commits
                    SELECT AuthorId AS UserId FROM Commits c WHERE c.Date >= @periodStartDate AND c.Date <= @periodEndDate AND c.IsRevert = 0{repoWhereClause}
                    UNION
                    -- PRs Created
                    SELECT pr.AuthorId AS UserId FROM PullRequests pr WHERE pr.CreatedOn >= @periodStartDate AND pr.CreatedOn <= @periodEndDate{repoWhereClause.Replace("c.RepositoryId", "pr.RepositoryId")}
                    UNION
                    -- PRs Approved
                    SELECT u.Id AS UserId
                    FROM PullRequestApprovals pra
                    JOIN PullRequests pr ON pra.PullRequestId = pr.Id
                    JOIN Users u ON pra.UserUuid = u.BitbucketUserId
                    WHERE pra.ApprovedOn >= @periodStartDate AND pra.ApprovedOn <= @periodEndDate AND pra.Approved = 1{repoWhereClause.Replace("c.RepositoryId", "pr.RepositoryId")}
                ) AS UserActivity
            ", new { periodStartDate, periodEndDate, repoSlug });

            // Get the total number of users
            var totalUserCount = await connection.QuerySingleAsync<int>("SELECT COUNT(*) FROM Users");

            // Inactive users = Total users - Active users
            return totalUserCount - activeUserCount;
        }

        private async Task<List<ApproverStats>> GetTopApprovers(SqlConnection connection, DateTime periodStartDate, DateTime periodEndDate, string? repoSlug)
        {
            var repoWhereClause = string.IsNullOrEmpty(repoSlug) ? "" : " AND pr.RepositoryId = (SELECT Id FROM Repositories WHERE Slug = @repoSlug)";
            var topApprovers = await connection.QueryAsync<ApproverStats>(@$"
                SELECT TOP 5 u.DisplayName AS UserName, COUNT(pra.Id) AS PrApprovalCount
                FROM PullRequestApprovals pra
                JOIN PullRequests pr ON pra.PullRequestId = pr.Id
                JOIN Users u ON pra.UserUuid = u.BitbucketUserId
                WHERE pr.CreatedOn >= @periodStartDate AND pr.CreatedOn <= @periodEndDate{repoWhereClause}
                GROUP BY u.DisplayName
                ORDER BY PrApprovalCount DESC
            ", new { periodStartDate, periodEndDate, repoSlug });

            return topApprovers.ToList();
        }

        private async Task<PrsMergedByWeekdayData> GetPrsMergedByWeekdayData(SqlConnection connection, DateTime periodStartDate, DateTime periodEndDate, string? repoSlug)
        {
            var repoWhereClause = string.IsNullOrEmpty(repoSlug) ? "" : " AND RepositoryId = (SELECT Id FROM Repositories WHERE Slug = @repoSlug)";
            var query = @$"
                SELECT DATENAME(dw, MergedOn) AS DayOfWeek, COUNT(*) AS PrCount
                FROM PullRequests
                WHERE MergedOn >= CAST(@periodStartDate AS DATETIME2) AND MergedOn <= CAST(@periodEndDate AS DATETIME2) AND State = 'MERGED'{repoWhereClause}
                GROUP BY DATENAME(dw, MergedOn), DATEPART(dw, MergedOn)
                ORDER BY DATEPART(dw, MergedOn);
            ";

            var mergedPrs = await connection.QueryAsync<WeekdayPrCount>(query, new { periodStartDate, periodEndDate, repoSlug });

            return new PrsMergedByWeekdayData
            {
                MergedPrsByWeekday = mergedPrs.ToList()
            };
        }
    }
} 