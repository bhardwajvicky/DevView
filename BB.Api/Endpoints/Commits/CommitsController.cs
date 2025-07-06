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
            int? userId = null,
            DateTime? startDate = null,
            DateTime? endDate = null)
        {
            if (page < 1) page = 1;
            if (pageSize < 1) pageSize = DefaultPageSize;

            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            // Get repo ID
            var repoId = await connection.QuerySingleOrDefaultAsync<int?>(
                "SELECT Id FROM Repositories WHERE Slug = @repoSlug", new { repoSlug });
            if (repoId == null)
                return NotFound($"Repository '{repoSlug}' not found.");

            // Build WHERE clause
            var where = "WHERE c.RepositoryId = @repoId";
            if (!includePR)
                where += " AND c.IsPRMergeCommit = 0";
            if (userId.HasValue)
                where += " AND c.AuthorId = @userId";
            if (startDate.HasValue)
                where += " AND c.Date >= @startDate";
            if (endDate.HasValue)
                where += " AND c.Date <= @endDate";

            // Count total commits
            var countSql = $"SELECT COUNT(*) FROM Commits c {where}";
            var totalCount = await connection.QuerySingleAsync<int>(countSql, new { repoId, userId, startDate, endDate });
            var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);

            // Query paginated commits with author info
            var sql = $@"
                SELECT c.BitbucketCommitHash AS Hash, c.Message, u.DisplayName AS AuthorName, c.Date, c.IsMerge, c.IsPRMergeCommit,
                       c.LinesAdded, c.LinesRemoved, c.CodeLinesAdded, c.CodeLinesRemoved
                FROM Commits c
                JOIN Users u ON c.AuthorId = u.Id
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
        }
        public class PaginatedCommitsResponse
        {
            public List<CommitListItemDto> Commits { get; set; } = new();
            public int TotalPages { get; set; }
        }
    }
}
