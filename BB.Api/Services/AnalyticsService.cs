using BB.Api.Endpoints.Analytics;
using BBIntegration.Common;
using Dapper;
using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.Linq;
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

        private static string GetFilterClause(bool includePR = true)
        {
            var prFilter = includePR ? "" : "c.IsPRMergeCommit = 0 AND ";
            return $@"
                WHERE 
                    {prFilter}(@RepoSlug IS NULL OR r.Slug = @RepoSlug)
                    AND (@Workspace IS NULL OR r.Workspace = @Workspace)
                    AND (@UserId IS NULL OR c.AuthorId = @UserId)
                    AND (@StartDate IS NULL OR c.Date >= @StartDate)
                    AND (@EndDate IS NULL OR c.Date <= @EndDate)
            ";
        }

        public async Task<IEnumerable<CommitActivityDto>> GetCommitActivityAsync(
            string repoSlug, string workspace, DateTime? startDate, DateTime? endDate, GroupingType groupBy, int? userId, 
            bool includePR = true, bool includeData = true, bool includeConfig = true)
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
                    SUM(c.CodeLinesRemoved) AS CodeLinesRemoved,
                    SUM(c.DataLinesAdded) AS DataLinesAdded,
                    SUM(c.DataLinesRemoved) AS DataLinesRemoved,
                    SUM(c.ConfigLinesAdded) AS ConfigLinesAdded,
                    SUM(c.ConfigLinesRemoved) AS ConfigLinesRemoved,
                    SUM(c.DocsLinesAdded) AS DocsLinesAdded,
                    SUM(c.DocsLinesRemoved) AS DocsLinesRemoved
                FROM Commits c
                JOIN Repositories r ON c.RepositoryId = r.Id
                {GetFilterClause(includePR)}
                GROUP BY CAST(DATEADD({dateTrunc}, DATEDIFF({dateTrunc}, 0, c.Date), 0) AS DATE)
                ORDER BY Date;
            ";

            return await connection.QueryAsync<CommitActivityDto>(sql, new { repoSlug, workspace, startDate, endDate, userId });
        }

        public async Task<IEnumerable<ContributorActivityDto>> GetContributorActivityAsync(
            string repoSlug, string workspace, DateTime? startDate, DateTime? endDate, GroupingType groupBy, int? userId,
            bool includePR = true, bool includeData = true, bool includeConfig = true)
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
                    SUM(c.CodeLinesRemoved) AS CodeLinesRemoved,
                    SUM(c.DataLinesAdded) AS DataLinesAdded,
                    SUM(c.DataLinesRemoved) AS DataLinesRemoved,
                    SUM(c.ConfigLinesAdded) AS ConfigLinesAdded,
                    SUM(c.ConfigLinesRemoved) AS ConfigLinesRemoved,
                    SUM(c.DocsLinesAdded) AS DocsLinesAdded,
                    SUM(c.DocsLinesRemoved) AS DocsLinesRemoved
                FROM Commits c
                JOIN Repositories r ON c.RepositoryId = r.Id
                JOIN Users u ON c.AuthorId = u.Id
                {GetFilterClause(includePR)}
                GROUP BY CAST(DATEADD({dateTrunc}, DATEDIFF({dateTrunc}, 0, c.Date), 0) AS DATE), u.Id, u.DisplayName
                ORDER BY Date, DisplayName;
            ";

            return await connection.QueryAsync<ContributorActivityDto>(sql, new { repoSlug, workspace, startDate, endDate, userId });
        }

        public async Task<IEnumerable<CommitPunchcardDto>> GetCommitPunchcardAsync(
            string repoSlug, string workspace, DateTime? startDate, DateTime? endDate, int? userId, bool includePR = true)
        {
            using var connection = new SqlConnection(_connectionString);

            var sql = $@"
                SELECT 
                    DATEPART(weekday, c.Date) - 1 AS DayOfWeek,
                    DATEPART(hour, c.Date) AS Hour,
                    COUNT(c.Id) AS CommitCount
                FROM Commits c
                JOIN Repositories r ON c.RepositoryId = r.Id
                {GetFilterClause(includePR)}
                GROUP BY DATEPART(weekday, c.Date), DATEPART(hour, c.Date)
                ORDER BY DayOfWeek, Hour;
            ";

            return await connection.QueryAsync<CommitPunchcardDto>(sql, new { repoSlug, workspace, startDate, endDate, userId });
        }

        public async Task<IEnumerable<RepositorySummaryDto>> GetRepositoriesAsync()
        {
            using var connection = new SqlConnection(_connectionString);
            const string sql = @"
                SELECT 
                    r.Name, 
                    r.Slug, 
                    r.Workspace,
                    MIN(c.Date) AS OldestCommitDate
                FROM Repositories r
                LEFT JOIN Commits c ON r.Id = c.RepositoryId
                GROUP BY r.Name, r.Slug, r.Workspace
                ORDER BY r.Name;";
            return await connection.QueryAsync<RepositorySummaryDto>(sql);
        }

        public async Task<IEnumerable<UserDto>> GetUsersAsync()
        {
            using var connection = new SqlConnection(_connectionString);
            const string sql = "SELECT Id, BitbucketUserId, DisplayName, AvatarUrl, CreatedOn FROM Users ORDER BY DisplayName;";
            return await connection.QueryAsync<UserDto>(sql);
        }

        public async Task<IEnumerable<string>> GetWorkspacesAsync()
        {
            using var connection = new SqlConnection(_connectionString);
            const string sql = "SELECT DISTINCT Workspace FROM Repositories ORDER BY Workspace;";
            return await connection.QueryAsync<string>(sql);
        }

        public async Task<IEnumerable<CommitDetailDto>> GetCommitDetailsAsync(
            string? repoSlug, string? workspace, int userId, DateTime date, DateTime? startDate, DateTime? endDate)
        {
            using var connection = new SqlConnection(_connectionString);

            const string sql = @"
                SELECT 
                    c.Id,
                    c.BitbucketCommitHash AS CommitHash,
                    c.Date,
                    c.Message,
                    u.DisplayName AS AuthorName,
                    r.Name AS RepositoryName,
                    r.Slug AS RepositorySlug,
                    c.LinesAdded,
                    c.LinesRemoved,
                    c.CodeLinesAdded,
                    c.CodeLinesRemoved,
                    c.DataLinesAdded,
                    c.DataLinesRemoved,
                    c.ConfigLinesAdded,
                    c.ConfigLinesRemoved,
                    c.DocsLinesAdded,
                    c.DocsLinesRemoved,
                    c.IsMerge
                FROM Commits c
                JOIN Users u ON c.AuthorId = u.Id
                JOIN Repositories r ON c.RepositoryId = r.Id
                WHERE c.AuthorId = @UserId 
                    AND CAST(c.Date AS DATE) = CAST(@Date AS DATE)
                    AND (@RepoSlug IS NULL OR r.Slug = @RepoSlug)
                    AND (@Workspace IS NULL OR r.Workspace = @Workspace)
                    AND (@StartDate IS NULL OR c.Date >= @StartDate)
                    AND (@EndDate IS NULL OR c.Date <= @EndDate)
                ORDER BY c.Date DESC;
            ";

            return await connection.QueryAsync<CommitDetailDto>(sql, new { repoSlug, workspace, userId, date, startDate, endDate });
        }

        public async Task<FileClassificationSummaryDto> GetFileClassificationSummaryAsync(
            string? repoSlug, string? workspace, DateTime? startDate, DateTime? endDate, int? userId, bool includePR = true)
        {
            using var connection = new SqlConnection(_connectionString);

            var sql = $@"
                SELECT 
                    COUNT(c.Id) AS TotalCommits,
                    SUM(c.LinesAdded) AS TotalLinesAdded,
                    SUM(c.LinesRemoved) AS TotalLinesRemoved,
                    
                    SUM(c.CodeLinesAdded) AS CodeLinesAdded,
                    SUM(c.CodeLinesRemoved) AS CodeLinesRemoved,
                    COUNT(CASE WHEN c.CodeLinesAdded > 0 OR c.CodeLinesRemoved > 0 THEN 1 END) AS CodeCommits,
                    
                    SUM(c.DataLinesAdded) AS DataLinesAdded,
                    SUM(c.DataLinesRemoved) AS DataLinesRemoved,
                    COUNT(CASE WHEN c.DataLinesAdded > 0 OR c.DataLinesRemoved > 0 THEN 1 END) AS DataCommits,
                    
                    SUM(c.ConfigLinesAdded) AS ConfigLinesAdded,
                    SUM(c.ConfigLinesRemoved) AS ConfigLinesRemoved,
                    COUNT(CASE WHEN c.ConfigLinesAdded > 0 OR c.ConfigLinesRemoved > 0 THEN 1 END) AS ConfigCommits,
                    
                    SUM(c.DocsLinesAdded) AS DocsLinesAdded,
                    SUM(c.DocsLinesRemoved) AS DocsLinesRemoved,
                    COUNT(CASE WHEN c.DocsLinesAdded > 0 OR c.DocsLinesRemoved > 0 THEN 1 END) AS DocsCommits
                FROM Commits c
                JOIN Repositories r ON c.RepositoryId = r.Id
                {GetFilterClause(includePR)}
            ";

            var result = await connection.QuerySingleOrDefaultAsync<FileClassificationSummaryDto>(sql, 
                new { repoSlug, workspace, startDate, endDate, userId });
            
            return result ?? new FileClassificationSummaryDto();
        }

        public async Task<IEnumerable<FileTypeActivityDto>> GetFileTypeActivityAsync(
            string? repoSlug, string? workspace, DateTime? startDate, DateTime? endDate, GroupingType groupBy, int? userId, bool includePR = true)
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
                WITH FileTypeData AS (
                    SELECT 
                        CAST(DATEADD({dateTrunc}, DATEDIFF({dateTrunc}, 0, c.Date), 0) AS DATE) AS Date,
                        'code' AS FileType,
                        COUNT(CASE WHEN c.CodeLinesAdded > 0 OR c.CodeLinesRemoved > 0 THEN 1 END) AS CommitCount,
                        SUM(c.CodeLinesAdded) AS LinesAdded,
                        SUM(c.CodeLinesRemoved) AS LinesRemoved
                    FROM Commits c
                    JOIN Repositories r ON c.RepositoryId = r.Id
                    {GetFilterClause(includePR)}
                    GROUP BY CAST(DATEADD({dateTrunc}, DATEDIFF({dateTrunc}, 0, c.Date), 0) AS DATE)
                    
                    UNION ALL
                    
                    SELECT 
                        CAST(DATEADD({dateTrunc}, DATEDIFF({dateTrunc}, 0, c.Date), 0) AS DATE) AS Date,
                        'data' AS FileType,
                        COUNT(CASE WHEN c.DataLinesAdded > 0 OR c.DataLinesRemoved > 0 THEN 1 END) AS CommitCount,
                        SUM(c.DataLinesAdded) AS LinesAdded,
                        SUM(c.DataLinesRemoved) AS LinesRemoved
                    FROM Commits c
                    JOIN Repositories r ON c.RepositoryId = r.Id
                    {GetFilterClause(includePR)}
                    GROUP BY CAST(DATEADD({dateTrunc}, DATEDIFF({dateTrunc}, 0, c.Date), 0) AS DATE)
                    
                    UNION ALL
                    
                    SELECT 
                        CAST(DATEADD({dateTrunc}, DATEDIFF({dateTrunc}, 0, c.Date), 0) AS DATE) AS Date,
                        'config' AS FileType,
                        COUNT(CASE WHEN c.ConfigLinesAdded > 0 OR c.ConfigLinesRemoved > 0 THEN 1 END) AS CommitCount,
                        SUM(c.ConfigLinesAdded) AS LinesAdded,
                        SUM(c.ConfigLinesRemoved) AS LinesRemoved
                    FROM Commits c
                    JOIN Repositories r ON c.RepositoryId = r.Id
                    {GetFilterClause(includePR)}
                    GROUP BY CAST(DATEADD({dateTrunc}, DATEDIFF({dateTrunc}, 0, c.Date), 0) AS DATE)
                    
                    UNION ALL
                    
                    SELECT 
                        CAST(DATEADD({dateTrunc}, DATEDIFF({dateTrunc}, 0, c.Date), 0) AS DATE) AS Date,
                        'docs' AS FileType,
                        COUNT(CASE WHEN c.DocsLinesAdded > 0 OR c.DocsLinesRemoved > 0 THEN 1 END) AS CommitCount,
                        SUM(c.DocsLinesAdded) AS LinesAdded,
                        SUM(c.DocsLinesRemoved) AS LinesRemoved
                    FROM Commits c
                    JOIN Repositories r ON c.RepositoryId = r.Id
                    {GetFilterClause(includePR)}
                    GROUP BY CAST(DATEADD({dateTrunc}, DATEDIFF({dateTrunc}, 0, c.Date), 0) AS DATE)
                )
                SELECT 
                    Date,
                    FileType,
                    CommitCount,
                    LinesAdded,
                    LinesRemoved,
                    (LinesAdded - LinesRemoved) AS NetLinesChanged
                FROM FileTypeData
                WHERE CommitCount > 0
                ORDER BY Date, FileType;
            ";

            return await connection.QueryAsync<FileTypeActivityDto>(sql, new { repoSlug, workspace, startDate, endDate, userId });
        }

        public async Task<TopCommittersResponseDto> GetTopBottomCommittersAsync(
            string? repoSlug, string? workspace, DateTime? startDate, DateTime? endDate, GroupingType groupBy,
            bool includePR = true, bool includeData = true, bool includeConfig = true, int topCount = 3, int bottomCount = 3)
        {
            using var connection = new SqlConnection(_connectionString);

            // First, get the ranking of committers based on the filters
            var rankingSql = GetCommitterRankingQuery(includePR, includeData, includeConfig);
            
            var allCommitters = await connection.QueryAsync<CommitterRankingDto>(rankingSql, 
                new { repoSlug, workspace, startDate, endDate });

            var committersList = allCommitters.ToList();
            
            // Get top and bottom committers
            var topCommitters = committersList.Take(topCount).ToList();
            var bottomCommitters = committersList.TakeLast(bottomCount).ToList();

            // Now get detailed activity data for each committer
            var result = new TopCommittersResponseDto();

            // Process top committers
            foreach (var committer in topCommitters)
            {
                var activityData = await GetContributorActivityAsync(repoSlug!, workspace!, startDate, endDate, groupBy, committer.UserId, includePR, includeData, includeConfig);
                
                result.TopCommitters.Add(new TopCommittersDto
                {
                    UserId = committer.UserId,
                    DisplayName = committer.DisplayName,
                    AvatarUrl = committer.AvatarUrl,
                    TotalCommits = committer.TotalCommits,
                    TotalLinesAdded = committer.TotalLinesAdded,
                    TotalLinesRemoved = committer.TotalLinesRemoved,
                    CodeLinesAdded = committer.CodeLinesAdded,
                    CodeLinesRemoved = committer.CodeLinesRemoved,
                    DataLinesAdded = committer.DataLinesAdded,
                    DataLinesRemoved = committer.DataLinesRemoved,
                    ConfigLinesAdded = committer.ConfigLinesAdded,
                    ConfigLinesRemoved = committer.ConfigLinesRemoved,
                    DocsLinesAdded = committer.DocsLinesAdded,
                    DocsLinesRemoved = committer.DocsLinesRemoved,
                    ActivityData = activityData.ToList()
                });
            }

            // Process bottom committers
            foreach (var committer in bottomCommitters)
            {
                var activityData = await GetContributorActivityAsync(repoSlug!, workspace!, startDate, endDate, groupBy, committer.UserId, includePR, includeData, includeConfig);
                
                result.BottomCommitters.Add(new TopCommittersDto
                {
                    UserId = committer.UserId,
                    DisplayName = committer.DisplayName,
                    AvatarUrl = committer.AvatarUrl,
                    TotalCommits = committer.TotalCommits,
                    TotalLinesAdded = committer.TotalLinesAdded,
                    TotalLinesRemoved = committer.TotalLinesRemoved,
                    CodeLinesAdded = committer.CodeLinesAdded,
                    CodeLinesRemoved = committer.CodeLinesRemoved,
                    DataLinesAdded = committer.DataLinesAdded,
                    DataLinesRemoved = committer.DataLinesRemoved,
                    ConfigLinesAdded = committer.ConfigLinesAdded,
                    ConfigLinesRemoved = committer.ConfigLinesRemoved,
                    DocsLinesAdded = committer.DocsLinesAdded,
                    DocsLinesRemoved = committer.DocsLinesRemoved,
                    ActivityData = activityData.ToList()
                });
            }

            return result;
        }

        public async Task<IEnumerable<PullRequestAnalysisDto>> GetPullRequestAnalysisAsync(
            string? repoSlug, string? workspace, DateTime? startDate, DateTime? endDate, string? state = null)
        {
            using var connection = new SqlConnection(_connectionString);

            var sql = @"
                SELECT 
                    pr.Id,
                    pr.Title,
                    pr.State,
                    pr.CreatedOn,
                    pr.UpdatedOn,
                    pr.MergedOn,
                    pr.ClosedOn,
                    u.Id AS AuthorId,
                    u.BitbucketUserId AS AuthorUuid,
                    u.DisplayName AS AuthorDisplayName,
                    u.AvatarUrl AS AuthorAvatarUrl,
                    u.CreatedOn AS AuthorCreatedOn,
                    a.UserId AS ApproverUserId,
                    au.BitbucketUserId AS ApproverUuid,
                    au.DisplayName AS ApproverDisplayName,
                    au.AvatarUrl AS ApproverAvatarUrl,
                    a.Role AS ApproverRole,
                    a.IsApproved,
                    a.ApprovedOn
                FROM PullRequests pr
                JOIN Repositories r ON pr.RepositoryId = r.Id
                JOIN Users u ON pr.AuthorId = u.Id
                LEFT JOIN PullRequestApprovals a ON pr.Id = a.PullRequestId
                LEFT JOIN Users au ON a.UserId = au.Id
                WHERE 
                    (@RepoSlug IS NULL OR r.Slug = @RepoSlug)
                    AND (@Workspace IS NULL OR r.Workspace = @Workspace)
                    AND (@StartDate IS NULL OR pr.CreatedOn >= @StartDate)
                    AND (@EndDate IS NULL OR pr.CreatedOn <= @EndDate)
                    AND (@State IS NULL OR pr.State = @State)
                ORDER BY pr.CreatedOn DESC;";

            var pullRequestDict = new Dictionary<long, PullRequestAnalysisDto>();

            await connection.QueryAsync<PullRequestAnalysisDto, UserDto, PullRequestApproverDto, PullRequestAnalysisDto>(
                sql,
                (pr, author, approver) =>
                {
                    if (!pullRequestDict.TryGetValue(pr.Id, out var existingPr))
                    {
                        existingPr = pr;
                        existingPr.Author = author;
                        existingPr.TimeToMerge = pr.MergedOn.HasValue ? pr.MergedOn.Value - pr.CreatedOn : null;
                        pullRequestDict.Add(pr.Id, existingPr);
                    }

                    if (approver != null)
                    {
                        existingPr.Approvers.Add(approver);
                    }

                    return existingPr;
                },
                new { repoSlug, workspace, startDate, endDate, state },
                splitOn: "AuthorId,ApproverUserId"
            );

            return pullRequestDict.Values;
        }

        public async Task<IEnumerable<RepositorySummaryDto>> GetTopOpenPullRequestsAsync(
            string? repoSlug, string? workspace, DateTime? startDate, DateTime? endDate)
        {
            using var connection = new SqlConnection(_connectionString);

            var sql = @"
                SELECT TOP 5
                    r.Name,
                    r.Slug,
                    r.Workspace,
                    COUNT(pr.Id) AS OpenPullRequestCount
                FROM Repositories r
                JOIN PullRequests pr ON r.Id = pr.RepositoryId
                WHERE pr.State = 'OPEN'
                    AND (@RepoSlug IS NULL OR r.Slug = @RepoSlug)
                    AND (@Workspace IS NULL OR r.Workspace = @Workspace)
                    AND (@StartDate IS NULL OR pr.CreatedOn >= @StartDate)
                    AND (@EndDate IS NULL OR pr.CreatedOn <= @EndDate)
                GROUP BY r.Name, r.Slug, r.Workspace
                ORDER BY OpenPullRequestCount DESC;";

            return await connection.QueryAsync<RepositorySummaryDto>(sql, new { repoSlug, workspace, startDate, endDate });
        }

        public async Task<IEnumerable<RepositorySummaryDto>> GetTopOldestOpenPullRequestsAsync(
            string? repoSlug, string? workspace, DateTime? startDate, DateTime? endDate)
        {
            using var connection = new SqlConnection(_connectionString);

            var sql = @"
                SELECT TOP 5
                    r.Name,
                    r.Slug,
                    r.Workspace,
                    MIN(pr.CreatedOn) AS OldestOpenPullRequestDate
                FROM Repositories r
                JOIN PullRequests pr ON r.Id = pr.RepositoryId
                WHERE pr.State = 'OPEN'
                    AND (@RepoSlug IS NULL OR r.Slug = @RepoSlug)
                    AND (@Workspace IS NULL OR r.Workspace = @Workspace)
                    AND (@StartDate IS NULL OR pr.CreatedOn >= @StartDate)
                    AND (@EndDate IS NULL OR pr.CreatedOn <= @EndDate)
                GROUP BY r.Name, r.Slug, r.Workspace
                ORDER BY OldestOpenPullRequestDate ASC;";

            return await connection.QueryAsync<RepositorySummaryDto>(sql, new { repoSlug, workspace, startDate, endDate });
        }

        public async Task<IEnumerable<RepositorySummaryDto>> GetTopUnapprovedPullRequestsAsync(
            string? repoSlug,
            string? workspace,
            DateTime? startDate,
            DateTime? endDate)
        {
            var sql = @"SELECT
                            r.Name,
                            COUNT(pr.Id) AS PRsMissingApprovalCount
                        FROM
                            PullRequests pr
                        JOIN
                            Repositories r ON pr.RepositoryId = r.Id
                        LEFT JOIN
                            PullRequestApprovals pa ON pr.Id = pa.PullRequestId AND pa.Approved = 1
                        WHERE
                            r.WorkspaceSlug = @workspace
                            AND (@repoSlug IS NULL OR r.Slug = @repoSlug)
                            AND (@startDate IS NULL OR pr.CreatedOn >= @startDate)
                            AND (@endDate IS NULL OR pr.CreatedOn <= @endDate)
                            AND pr.Status = 'OPEN'
                            AND (pa.ActualApprovals IS NULL OR pa.ActualApprovals = 0)
                        GROUP BY
                            r.Name
                        ORDER BY
                            PRsMissingApprovalCount DESC
                        OFFSET 0 ROWS FETCH NEXT 5 ROWS ONLY;";

            using var connection = new SqlConnection(_connectionString);
            return await connection.QueryAsync<RepositorySummaryDto>(sql, new { repoSlug, workspace, startDate, endDate });
        }

        public async Task<IEnumerable<PrAgeBubbleDto>> GetPrAgeBubbleDataAsync(
            string? repoSlug, string? workspace, DateTime? startDate, DateTime? endDate)
        {
            using var connection = new SqlConnection(_connectionString);

            var sql = @"
                SELECT
                    DATEDIFF(day, pr.CreatedOn, GETUTCDATE()) AS AgeInDays,
                    COUNT(pr.Id) AS NumberOfPRs,
                    r.Name AS RepositoryName,
                    r.Slug AS RepositorySlug,
                    r.Workspace
                FROM PullRequests pr
                JOIN Repositories r ON pr.RepositoryId = r.Id
                WHERE pr.State = 'OPEN'
                    AND (@RepoSlug IS NULL OR r.Slug = @RepoSlug)
                    AND (@Workspace IS NULL OR r.Workspace = @Workspace)
                    AND (@StartDate IS NULL OR pr.CreatedOn >= @StartDate)
                    AND (@EndDate IS NULL OR pr.CreatedOn <= @EndDate)
                    AND DATEDIFF(day, pr.CreatedOn, GETUTCDATE()) BETWEEN 1 AND 20 -- PR age between 1 and 20 days
                GROUP BY DATEDIFF(day, pr.CreatedOn, GETUTCDATE()), r.Name, r.Slug, r.Workspace
                ORDER BY AgeInDays ASC;";

            return await connection.QueryAsync<PrAgeBubbleDto>(sql, new { repoSlug, workspace, startDate, endDate });
        }

        private string GetCommitterRankingQuery(bool includePR, bool includeData, bool includeConfig)
        {
            // Build the ranking criteria based on filters
            var rankingCriteria = "SUM(c.LinesAdded + c.CodeLinesAdded)"; // Default: Total + Code
            
            if (includeData && includeConfig)
            {
                rankingCriteria = "SUM(c.LinesAdded + c.CodeLinesAdded + c.DataLinesAdded + c.ConfigLinesAdded)";
            }
            else if (includeData)
            {
                rankingCriteria = "SUM(c.LinesAdded + c.CodeLinesAdded + c.DataLinesAdded)";
            }
            else if (includeConfig)
            {
                rankingCriteria = "SUM(c.LinesAdded + c.CodeLinesAdded + c.ConfigLinesAdded)";
            }

            // Create a custom filter clause without the userId parameter for ranking
            var prFilter = includePR ? "" : "c.IsPRMergeCommit = 0 AND ";
            var filterClause = $@"
                WHERE 
                    {prFilter}(@RepoSlug IS NULL OR r.Slug = @RepoSlug)
                    AND (@Workspace IS NULL OR r.Workspace = @Workspace)
                    AND (@StartDate IS NULL OR c.Date >= @StartDate)
                    AND (@EndDate IS NULL OR c.Date <= @EndDate)
            ";

            return $@"
                SELECT 
                    u.Id AS UserId,
                    u.DisplayName,
                    u.AvatarUrl,
                    COUNT(c.Id) AS TotalCommits,
                    SUM(c.LinesAdded) AS TotalLinesAdded,
                    SUM(c.LinesRemoved) AS TotalLinesRemoved,
                    SUM(c.CodeLinesAdded) AS CodeLinesAdded,
                    SUM(c.CodeLinesRemoved) AS CodeLinesRemoved,
                    SUM(c.DataLinesAdded) AS DataLinesAdded,
                    SUM(c.DataLinesRemoved) AS DataLinesRemoved,
                    SUM(c.ConfigLinesAdded) AS ConfigLinesAdded,
                    SUM(c.ConfigLinesRemoved) AS ConfigLinesRemoved,
                    SUM(c.DocsLinesAdded) AS DocsLinesAdded,
                    SUM(c.DocsLinesRemoved) AS DocsLinesRemoved,
                    {rankingCriteria} AS RankingScore
                FROM Commits c
                JOIN Repositories r ON c.RepositoryId = r.Id
                JOIN Users u ON c.AuthorId = u.Id
                {filterClause}
                GROUP BY u.Id, u.DisplayName, u.AvatarUrl
                HAVING COUNT(c.Id) > 0
                ORDER BY RankingScore DESC;
            ";
        }

        private class CommitterRankingDto
        {
            public int UserId { get; set; }
            public string DisplayName { get; set; } = string.Empty;
            public string AvatarUrl { get; set; } = string.Empty;
            public int TotalCommits { get; set; }
            public int TotalLinesAdded { get; set; }
            public int TotalLinesRemoved { get; set; }
            public int CodeLinesAdded { get; set; }
            public int CodeLinesRemoved { get; set; }
            public int DataLinesAdded { get; set; }
            public int DataLinesRemoved { get; set; }
            public int ConfigLinesAdded { get; set; }
            public int ConfigLinesRemoved { get; set; }
            public int DocsLinesAdded { get; set; }
            public int DocsLinesRemoved { get; set; }
            public int RankingScore { get; set; }
        }
    }
} 