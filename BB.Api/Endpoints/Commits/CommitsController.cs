using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using Microsoft.Data.SqlClient;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using Microsoft.Extensions.Configuration;

namespace BB.Api.Endpoints.Commits
{
    [ApiController]
    [Route("api/[controller]")]
    public class CommitsController : ControllerBase
    {
        private readonly string _connectionString;
        private const int DefaultPageSize = 25;

        public CommitsController(IConfiguration config)
        {
            _connectionString = config.GetConnectionString("DefaultConnection");
        }

        [HttpGet("{repoSlug}")]
        public async Task<IActionResult> GetCommits(
            string repoSlug,
            int page = 1,
            int pageSize = DefaultPageSize,
            bool includePR = true,
            bool includeData = true,
            bool includeConfig = true,
            int? userId = null,
            DateTime? startDate = null,
            DateTime? endDate = null)
        {
            if (page < 1) page = 1;
            if (pageSize < 1) pageSize = DefaultPageSize;

            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            // Handle "all" repositories case
            int? repoId = null;
            if (!string.Equals(repoSlug, "all", StringComparison.OrdinalIgnoreCase))
            {
                // Get specific repo ID
                repoId = await connection.QuerySingleOrDefaultAsync<int?>(
                    "SELECT Id FROM Repositories WHERE Slug = @repoSlug", new { repoSlug });
                if (repoId == null)
                    return NotFound($"Repository '{repoSlug}' not found.");
            }

            // Build WHERE clause
            var where = "WHERE 1=1";
            if (repoId.HasValue)
                where += " AND c.RepositoryId = @repoId";
            if (!includePR)
                where += " AND c.IsPRMergeCommit = 0";
            if (userId.HasValue)
                where += " AND c.AuthorId = @userId";
            if (startDate.HasValue)
                where += " AND c.Date >= @startDate";
            if (endDate.HasValue)
                where += " AND c.Date <= @endDate";

            // Count total commits
            var countSql = $"SELECT COUNT(*) FROM Commits c JOIN Repositories r ON c.RepositoryId = r.Id {where}";
            var totalCount = await connection.QuerySingleAsync<int>(countSql, new { repoId, userId, startDate, endDate });
            var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);

            // Query paginated commits with author info and repository info
            var sql = $@"
                SELECT c.BitbucketCommitHash AS Hash, c.Message, u.DisplayName AS AuthorName, c.Date, c.IsMerge, c.IsPRMergeCommit,
                       c.LinesAdded, c.LinesRemoved, c.CodeLinesAdded, c.CodeLinesRemoved, 
                       c.DataLinesAdded, c.DataLinesRemoved, c.ConfigLinesAdded, c.ConfigLinesRemoved,
                       c.DocsLinesAdded, c.DocsLinesRemoved, r.Name AS RepositoryName, r.Slug AS RepositorySlug
                FROM Commits c
                JOIN Users u ON c.AuthorId = u.Id
                JOIN Repositories r ON c.RepositoryId = r.Id
                {where}
                ORDER BY c.Date DESC
                OFFSET @offset ROWS FETCH NEXT @pageSize ROWS ONLY
            ";
            var commitList = (await connection.QueryAsync<CommitListItemDto>(sql, new
            {
                repoId,
                userId,
                startDate,
                endDate,
                offset = (page - 1) * pageSize,
                pageSize
            })).ToList();

            var response = new PaginatedCommitsResponse
            {
                Commits = commitList,
                TotalPages = totalPages
            };
            return Ok(response);
        }

        public class CommitListItemDto
        {
            public string Hash { get; set; } = string.Empty;
            public string Message { get; set; } = string.Empty;
            public string AuthorName { get; set; } = string.Empty;
            public DateTime Date { get; set; }
            public bool IsMerge { get; set; }
            public bool IsPRMergeCommit { get; set; }
            public int LinesAdded { get; set; }
            public int LinesRemoved { get; set; }
            public int CodeLinesAdded { get; set; }
            public int CodeLinesRemoved { get; set; }
            public int DataLinesAdded { get; set; }
            public int DataLinesRemoved { get; set; }
            public int ConfigLinesAdded { get; set; }
            public int ConfigLinesRemoved { get; set; }
            public int DocsLinesAdded { get; set; }
            public int DocsLinesRemoved { get; set; }
            public string? RepositoryName { get; set; }
            public string? RepositorySlug { get; set; }
        }
        public class PaginatedCommitsResponse
        {
            public List<CommitListItemDto> Commits { get; set; } = new();
            public int TotalPages { get; set; }
        }
    }
}
