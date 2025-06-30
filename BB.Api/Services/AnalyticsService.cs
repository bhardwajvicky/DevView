using BB.Api.Endpoints.Analytics;
using BBIntegration.Common;
using Dapper;
using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace BB.Api.Services
{
    public class AnalyticsService
    {
        private readonly string _connectionString;

        public AnalyticsService(BitbucketConfig config)
        {
            _connectionString = config.DbConnectionString;
        }

        public async Task<IEnumerable<CommitActivityDto>> GetCommitActivityAsync(
            string repoSlug, string workspace, DateTime? startDate, DateTime? endDate, GroupingType groupBy, int? userId)
        {
            using var connection = new SqlConnection(_connectionString);
            
            string dateTrunc;
            switch (groupBy)
            {
                case GroupingType.Day:
                    dateTrunc = "day";
                    break;
                case GroupingType.Month:
                    dateTrunc = "month";
                    break;
                case GroupingType.Week:
                default:
                    dateTrunc = "week";
                    break;
            }

            var sql = $@"
                SELECT 
                    CAST(DATEADD({dateTrunc}, DATEDIFF({dateTrunc}, 0, c.Date), 0) AS DATE) AS Date,
                    COUNT(c.Id) AS CommitCount,
                    SUM(c.LinesAdded) AS TotalLinesAdded,
                    SUM(c.LinesRemoved) AS TotalLinesRemoved,
                    SUM(c.CodeLinesAdded) AS CodeLinesAdded,
                    SUM(c.CodeLinesRemoved) AS CodeLinesRemoved
                FROM Commits c
                JOIN Repositories r ON c.RepositoryId = r.Id
                WHERE 
                    c.IsMerge = 0
                    AND (@RepoSlug IS NULL OR r.Slug = @RepoSlug)
                    AND (@RepoSlug IS NOT NULL OR r.Workspace = @Workspace)
                    AND (@UserId IS NULL OR c.AuthorId = @UserId)
                    AND (@StartDate IS NULL OR c.Date >= @StartDate)
                    AND (@EndDate IS NULL OR c.Date <= @EndDate)
                GROUP BY CAST(DATEADD({dateTrunc}, DATEDIFF({dateTrunc}, 0, c.Date), 0) AS DATE)
                ORDER BY Date;
            ";

            return await connection.QueryAsync<CommitActivityDto>(sql, new { repoSlug, workspace, startDate, endDate, userId });
        }

        public async Task<IEnumerable<ContributorActivityDto>> GetContributorActivityAsync(
            string repoSlug, string workspace, DateTime? startDate, DateTime? endDate, GroupingType groupBy, int? userId)
        {
            using var connection = new SqlConnection(_connectionString);

            string dateTrunc;
            switch (groupBy)
            {
                case GroupingType.Day:
                    dateTrunc = "day";
                    break;
                case GroupingType.Month:
                    dateTrunc = "month";
                    break;
                case GroupingType.Week:
                default:
                    dateTrunc = "week";
                    break;
            }

            var sql = $@"
                SELECT 
                    CAST(DATEADD({dateTrunc}, DATEDIFF({dateTrunc}, 0, c.Date), 0) AS DATE) AS Date,
                    u.Id AS UserId,
                    u.DisplayName,
                    COUNT(c.Id) AS CommitCount,
                    SUM(c.LinesAdded) AS TotalLinesAdded,
                    SUM(c.LinesRemoved) AS TotalLinesRemoved,
                    SUM(c.CodeLinesAdded) AS CodeLinesAdded,
                    SUM(c.CodeLinesRemoved) AS CodeLinesRemoved
                FROM Commits c
                JOIN Repositories r ON c.RepositoryId = r.Id
                JOIN Users u ON c.AuthorId = u.Id
                WHERE 
                    c.IsMerge = 0
                    AND (@RepoSlug IS NULL OR r.Slug = @RepoSlug)
                    AND (@RepoSlug IS NOT NULL OR r.Workspace = @Workspace)
                    AND (@UserId IS NULL OR u.Id = @UserId)
                    AND (@StartDate IS NULL OR c.Date >= @StartDate)
                    AND (@EndDate IS NULL OR c.Date <= @EndDate)
                GROUP BY CAST(DATEADD({dateTrunc}, DATEDIFF({dateTrunc}, 0, c.Date), 0) AS DATE), u.Id, u.DisplayName
                ORDER BY Date, DisplayName;
            ";

            return await connection.QueryAsync<ContributorActivityDto>(sql, new { repoSlug, workspace, startDate, endDate, userId });
        }

        public async Task<IEnumerable<CommitPunchcardDto>> GetCommitPunchcardAsync(
            string repoSlug, string workspace, DateTime? startDate, DateTime? endDate, int? userId)
        {
            using var connection = new SqlConnection(_connectionString);

            const string sql = @"
                SELECT 
                    DATEPART(weekday, c.Date) - 1 AS DayOfWeek,
                    DATEPART(hour, c.Date) AS Hour,
                    COUNT(c.Id) AS CommitCount
                FROM Commits c
                JOIN Repositories r ON c.RepositoryId = r.Id
                WHERE 
                    c.IsMerge = 0
                    AND (@RepoSlug IS NULL OR r.Slug = @RepoSlug)
                    AND (@RepoSlug IS NOT NULL OR r.Workspace = @Workspace)
                    AND (@UserId IS NULL OR c.AuthorId = @UserId)
                    AND (@StartDate IS NULL OR c.Date >= @StartDate)
                    AND (@EndDate IS NULL OR c.Date <= @EndDate)
                GROUP BY DATEPART(weekday, c.Date), DATEPART(hour, c.Date)
                ORDER BY DayOfWeek, Hour;
            ";

            return await connection.QueryAsync<CommitPunchcardDto>(sql, new { repoSlug, workspace, startDate, endDate, userId });
        }
    }
} 