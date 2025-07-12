using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using Microsoft.Extensions.Configuration;
using Microsoft.Data.SqlClient;

namespace BB.Api.Endpoints.Analytics
{
    [ApiController]
    [Route("api/[controller]")]
    public class UserDashboardController : ControllerBase
    {
        private readonly string _connectionString;

        public UserDashboardController(IConfiguration config)
        {
            _connectionString = config.GetConnectionString("DefaultConnection") ??
                                throw new InvalidOperationException("DefaultConnection connection string not found.");
        }

        [HttpGet]
        public async Task<IActionResult> GetUserDashboard(
            DateTime? startDate = null,
            DateTime? endDate = null)
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

            var currentPeriodStats = await GetPeriodStats(connection, startDate.Value, endDate.Value);
            var previousPeriodStats = await GetPeriodStats(connection, previousPeriodStartDate, previousPeriodEndDate);
            var prAgeGraphData = await GetPrAgeGraphData(connection, startDate.Value, endDate.Value);
            var topContributors = await GetTopContributors(connection, startDate.Value, endDate.Value);
            var usersWithNoActivity = await GetUsersWithNoActivity(connection, startDate.Value, endDate.Value);
            var topApprovers = await GetTopApprovers(connection, startDate.Value, endDate.Value);

            var response = new UserDashboardResponseDto
            {
                CurrentPeriod = currentPeriodStats,
                PreviousPeriod = previousPeriodStats,
                PrAgeGraphData = prAgeGraphData,
                TopContributors = topContributors,
                UsersWithNoActivity = usersWithNoActivity,
                TopApprovers = topApprovers
            };

            return Ok(response);
        }

        private async Task<PeriodStats> GetPeriodStats(SqlConnection connection, DateTime periodStartDate, DateTime periodEndDate)
        {
            // Implement logic to calculate KPIs for a given period
            // This will include ActiveContributingUsers, TotalLicensedUsers, TotalCommits, RepositoriesUpdated, PrsNotApprovedAndMerged

            // Total Commits
            var totalCommits = await connection.QuerySingleOrDefaultAsync<int>(
                "SELECT COUNT(*) FROM Commits WHERE Date >= @periodStartDate AND Date <= @periodEndDate AND IsRevert = 0", 
                new { periodStartDate, periodEndDate });

            // Repositories Updated
            var repositoriesUpdated = await connection.QuerySingleOrDefaultAsync<int>(
                "SELECT COUNT(DISTINCT RepositoryId) FROM Commits WHERE Date >= @periodStartDate AND Date <= @periodEndDate AND IsRevert = 0", 
                new { periodStartDate, periodEndDate });

            // Active Contributing Users
            var activeContributingUsers = await connection.QuerySingleOrDefaultAsync<int>(
                "SELECT COUNT(DISTINCT AuthorId) FROM Commits WHERE Date >= @periodStartDate AND Date <= @periodEndDate AND IsRevert = 0", 
                new { periodStartDate, periodEndDate });

            // Total Licensed Users - This needs to be determined. For now, let's assume it's the total number of users in the system.
            var totalLicensedUsers = await connection.QuerySingleOrDefaultAsync<int>(
                "SELECT COUNT(*) FROM Users WHERE CreatedOn <= @periodEndDate", new { periodEndDate });

            // PRs Not Approved and Merged
            var prsNotApprovedAndMerged = await connection.QuerySingleOrDefaultAsync<int>(
                "SELECT COUNT(*) FROM PullRequests WHERE CreatedOn >= @periodStartDate AND CreatedOn <= @periodEndDate AND State != 'MERGED' AND State != 'DECLINED'", 
                new { periodStartDate, periodEndDate });

            return new PeriodStats
            {
                StartDate = periodStartDate,
                EndDate = periodEndDate,
                TotalCommits = totalCommits,
                RepositoriesUpdated = repositoriesUpdated,
                ActiveContributingUsers = activeContributingUsers,
                TotalLicensedUsers = totalLicensedUsers, 
                PrsNotApprovedAndMerged = prsNotApprovedAndMerged 
            };
        }

        private async Task<PrAgeGraph> GetPrAgeGraphData(SqlConnection connection, DateTime periodStartDate, DateTime periodEndDate)
        {
            var openPrAgeData = new List<PrAgeDataPoint>();
            var mergedPrAgeData = new List<PrAgeDataPoint>();

            // Open PRs Age
            var openPrs = await connection.QueryAsync<DateTime>(
                "SELECT CreatedOn FROM PullRequests WHERE CreatedOn >= @periodStartDate AND CreatedOn <= @periodEndDate AND State = 'OPEN'",
                new { periodStartDate, periodEndDate });

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
            var mergedPrs = await connection.QueryAsync<(DateTime CreatedOn, DateTime MergedOn)>(
                "SELECT CreatedOn, MergedOn FROM PullRequests WHERE MergedOn >= @periodStartDate AND MergedOn <= @periodEndDate AND State = 'MERGED'",
                new { periodStartDate, periodEndDate });

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

        private async Task<List<ContributorStats>> GetTopContributors(SqlConnection connection, DateTime periodStartDate, DateTime periodEndDate)
        {
            var topContributors = await connection.QueryAsync<ContributorStats>(@"
                SELECT TOP 5 u.DisplayName AS UserName, COUNT(c.Id) AS Commits,
                       ISNULL(SUM(cf.LinesAdded), 0) AS CodeLinesAdded,
                       ISNULL(SUM(cf.LinesRemoved), 0) AS CodeLinesRemoved
                FROM Commits c
                JOIN Users u ON c.AuthorId = u.Id
                LEFT JOIN CommitFiles cf ON c.Id = cf.CommitId AND cf.FileType = 'code' AND cf.ExcludeFromReporting = 0
                WHERE c.Date >= @periodStartDate AND c.Date <= @periodEndDate AND c.IsRevert = 0
                GROUP BY u.DisplayName
                ORDER BY Commits DESC, CodeLinesAdded DESC
            ", new { periodStartDate, periodEndDate });

            return topContributors.ToList();
        }

        private async Task<int> GetUsersWithNoActivity(SqlConnection connection, DateTime periodStartDate, DateTime periodEndDate)
        {
            // Users who have no commits in the given period, but have existed before the period ends.
            var usersWithActivity = await connection.QueryAsync<int>(@"
                SELECT DISTINCT AuthorId FROM Commits
                WHERE Date >= @periodStartDate AND Date <= @periodEndDate
            ", new { periodStartDate = periodStartDate, periodEndDate = periodEndDate });

            var totalUsers = await connection.QuerySingleOrDefaultAsync<int>("SELECT COUNT(*) FROM Users WHERE CreatedOn <= @periodEndDate", new { periodEndDate = periodEndDate });

            return totalUsers - usersWithActivity.Count();
        }

        private async Task<List<ApproverStats>> GetTopApprovers(SqlConnection connection, DateTime periodStartDate, DateTime periodEndDate)
        {
            var topApprovers = await connection.QueryAsync<ApproverStats>(@"
                SELECT TOP 5 u.DisplayName AS UserName, COUNT(pra.Id) AS PrApprovalCount
                FROM PullRequestApprovals pra
                JOIN PullRequests pr ON pra.PullRequestId = pr.Id
                JOIN Users u ON pra.UserUuid = u.BitbucketUserId
                WHERE pr.CreatedOn >= @periodStartDate AND pr.CreatedOn <= @periodEndDate
                GROUP BY u.DisplayName
                ORDER BY PrApprovalCount DESC
            ", new { periodStartDate, periodEndDate });

            return topApprovers.ToList();
        }
    }
} 