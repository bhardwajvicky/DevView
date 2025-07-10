using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using Microsoft.Data.SqlClient;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using Microsoft.Extensions.Configuration;

namespace BB.Api.Endpoints.PullRequests
{
    [ApiController]
    [Route("api/[controller]")]
    public class PullRequestsController : ControllerBase
    {
        private readonly string _connectionString;
        private const int DefaultPageSize = 25;

        public PullRequestsController(IConfiguration config)
        {
            _connectionString = config.GetConnectionString("DefaultConnection");
        }

        [HttpGet("{repoSlug}")]
        public async Task<IActionResult> GetPullRequests(string repoSlug, int page = 1, int pageSize = DefaultPageSize)
        {
            if (page < 1) page = 1;
            if (pageSize < 1) pageSize = DefaultPageSize;

            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            if (repoSlug.ToLower() == "all")
            {
                // Count total PRs
                var totalCountAll = await connection.QuerySingleAsync<int>(
                    "SELECT COUNT(*) FROM PullRequests");
                var totalPagesAll = (int)Math.Ceiling(totalCountAll / (double)pageSize);

                // Query paginated PRs with author, repo, and workspace info
                var sqlAll = @"
                    SELECT pr.Id, pr.BitbucketPrId, pr.Title, pr.State, pr.CreatedOn, pr.UpdatedOn, u.DisplayName AS AuthorName,
                           r.Slug AS RepositorySlug, r.Workspace
                    FROM PullRequests pr
                    JOIN Users u ON pr.AuthorId = u.Id
                    JOIN Repositories r ON pr.RepositoryId = r.Id
                    ORDER BY pr.CreatedOn DESC
                    OFFSET @offset ROWS FETCH NEXT @pageSize ROWS ONLY
                ";
                var prListAll = (await connection.QueryAsync<PullRequestListItemDto>(sqlAll, new
                {
                    offset = (page - 1) * pageSize,
                    pageSize
                })).ToList();

                var responseAll = new PaginatedPullRequestsResponse
                {
                    PullRequests = prListAll,
                    TotalPages = totalPagesAll
                };
                return Ok(responseAll);
            }

            // Get repo ID
            var repoId = await connection.QuerySingleOrDefaultAsync<int?>(
                "SELECT Id FROM Repositories WHERE Slug = @repoSlug", new { repoSlug });
            if (repoId == null)
                return NotFound($"Repository '{repoSlug}' not found.");

            // Count total PRs
            var totalCount = await connection.QuerySingleAsync<int>(
                "SELECT COUNT(*) FROM PullRequests WHERE RepositoryId = @repoId", new { repoId });
            var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);

            // Query paginated PRs with author info, repo slug, and workspace
            var sql = @"
                SELECT pr.Id, pr.BitbucketPrId, pr.Title, pr.State, pr.CreatedOn, pr.UpdatedOn, u.DisplayName AS AuthorName,
                       r.Slug AS RepositorySlug, r.Workspace
                FROM PullRequests pr
                JOIN Users u ON pr.AuthorId = u.Id
                JOIN Repositories r ON pr.RepositoryId = r.Id
                WHERE pr.RepositoryId = @repoId
                ORDER BY pr.CreatedOn DESC
                OFFSET @offset ROWS FETCH NEXT @pageSize ROWS ONLY
            ";
            var prList = (await connection.QueryAsync<PullRequestListItemDto>(sql, new
            {
                repoId,
                offset = (page - 1) * pageSize,
                pageSize
            })).ToList();

            var response = new PaginatedPullRequestsResponse
            {
                PullRequests = prList,
                TotalPages = totalPages
            };
            return Ok(response);
        }

        public class PullRequestListItemDto
        {
            public int Id { get; set; }
            public string Title { get; set; } = string.Empty;
            public string AuthorName { get; set; } = string.Empty;
            public string State { get; set; } = string.Empty;
            public DateTime CreatedOn { get; set; }
            public DateTime? UpdatedOn { get; set; }
            public string RepositorySlug { get; set; } = string.Empty;
            public string Workspace { get; set; } = string.Empty;
            public string BitbucketPrId { get; set; } = string.Empty;
        }
        public class PaginatedPullRequestsResponse
        {
            public List<PullRequestListItemDto> PullRequests { get; set; } = new();
            public int TotalPages { get; set; }
        }
    }
}
