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
            int repoId, string workspace, DateTime? startDate, DateTime? endDate, GroupingType groupBy)
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
                    SUM(c.LinesAdded) AS LinesAdded,
                    SUM(c.LinesRemoved) AS LinesRemoved
                FROM Commits c
                JOIN Repositories r ON c.RepositoryId = r.Id
                WHERE 
                    c.IsMerge = 0
                    AND (@RepoId = 0 OR c.RepositoryId = @RepoId)
                    AND (@RepoId != 0 OR r.Workspace = @Workspace)
                    AND (@StartDate IS NULL OR c.Date >= @StartDate)
                    AND (@EndDate IS NULL OR c.Date <= @EndDate)
                GROUP BY CAST(DATEADD({dateTrunc}, DATEDIFF({dateTrunc}, 0, c.Date), 0) AS DATE)
                ORDER BY Date;
            ";

            return await connection.QueryAsync<CommitActivityDto>(sql, new { repoId, workspace, startDate, endDate });
        }
    }
} 