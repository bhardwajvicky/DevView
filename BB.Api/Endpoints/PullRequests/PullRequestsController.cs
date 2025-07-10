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

            var prDictionary = new Dictionary<int, PullRequestListItemDto>(); // Declare once
            int totalCount = 0; // Declare totalCount at method scope
            int totalPages = 1; // Declare totalPages at method scope

            if (repoSlug.ToLower() == "all")
            {
                // Count total PRs (distinct to avoid counting approvals as separate PRs)
                totalCount = await connection.QuerySingleAsync<int>(
                    "SELECT COUNT(DISTINCT pr.Id) FROM PullRequests pr");
                totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);

                // Query paginated PRs with author, repo, workspace, and approval info
                var sqlAll = @"
                    SELECT 
                        pr.Id, pr.BitbucketPrId, pr.Title, pr.State, pr.CreatedOn, pr.UpdatedOn,
                        u.DisplayName AS AuthorName, r.Name AS RepositoryName, r.Slug AS RepositorySlug, r.Workspace,
                        pa.DisplayName, pa.Role, pa.Approved, pa.ApprovedOn -- Approval details
                    FROM PullRequests pr
                    JOIN Users u ON pr.AuthorId = u.Id
                    JOIN Repositories r ON pr.RepositoryId = r.Id
                    LEFT JOIN PullRequestApprovals pa ON pr.Id = pa.PullRequestId
                    ORDER BY pr.CreatedOn DESC
                    OFFSET @offset ROWS FETCH NEXT @pageSize ROWS ONLY;

                    SELECT COUNT(*) FROM PullRequestApprovals WHERE PullRequestId = @PrId;
                ";

                // Using Dapper Multi-Mapping to map PRs and their approvals
                await connection.QueryAsync<PullRequestListItemDto, ApprovalDto, PullRequestListItemDto>(
                    sqlAll,
                    (pr, approval) =>
                    {
                        if (!prDictionary.TryGetValue(pr.Id, out var currentPr))
                        {
                            currentPr = pr;
                            prDictionary.Add(pr.Id, currentPr);
                        }

                        if (approval != null)
                        {
                            currentPr.Approvals.Add(approval);
                        }
                        return currentPr;
                    },
                    new { offset = (page - 1) * pageSize, pageSize },
                    splitOn: "DisplayName" // Split on the DisplayName column of ApprovalDto
                );

                // No need to create prListAll here, will be done after the if/else
                // No need to calculate ApprovalCount here, will be done after the if/else
            }
            else
            {
                // Get repo ID
                var repoId = await connection.QuerySingleOrDefaultAsync<int?>(
                    "SELECT Id FROM Repositories WHERE Slug = @repoSlug", new { repoSlug });
                if (repoId == null)
                    return NotFound($"Repository '{repoSlug}' not found.");

                // Count total PRs (distinct to avoid counting approvals as separate PRs)
                totalCount = await connection.QuerySingleAsync<int>(
                    "SELECT COUNT(DISTINCT pr.Id) FROM PullRequests pr WHERE pr.RepositoryId = @repoId", new { repoId });
                totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);

                // Query paginated PRs with author info, repo slug, workspace, and approval info
                var sql = @"
                    SELECT 
                        pr.Id, pr.BitbucketPrId, pr.Title, pr.State, pr.CreatedOn, pr.UpdatedOn,
                        u.DisplayName AS AuthorName, r.Name AS RepositoryName, r.Slug AS RepositorySlug, r.Workspace,
                        pa.DisplayName, pa.Role, pa.Approved, pa.ApprovedOn -- Approval details
                    FROM PullRequests pr
                    JOIN Users u ON pr.AuthorId = u.Id
                    JOIN Repositories r ON pr.RepositoryId = r.Id
                    LEFT JOIN PullRequestApprovals pa ON pr.Id = pa.PullRequestId
                    WHERE pr.RepositoryId = @repoId
                    ORDER BY pr.CreatedOn DESC
                    OFFSET @offset ROWS FETCH NEXT @pageSize ROWS ONLY;

                    SELECT COUNT(*) FROM PullRequestApprovals WHERE PullRequestId = @PrId;
                ";

                // Using Dapper Multi-Mapping to map PRs and their approvals
                await connection.QueryAsync<PullRequestListItemDto, ApprovalDto, PullRequestListItemDto>(
                    sql,
                    (pr, approval) =>
                    {
                        if (!prDictionary.TryGetValue(pr.Id, out var currentPr))
                        {
                            currentPr = pr;
                            prDictionary.Add(pr.Id, currentPr);
                        }

                        if (approval != null)
                        {
                            currentPr.Approvals.Add(approval);
                        }
                        return currentPr;
                    },
                    new 
                    {
                        repoId,
                        offset = (page - 1) * pageSize,
                        pageSize
                    },
                    splitOn: "DisplayName" // Split on the DisplayName column of ApprovalDto
                );

                // No need to create prList here, will be done after the if/else
                // No need to calculate ApprovalCount here, will be done after the if/else
            }

            var prList = prDictionary.Values.ToList();

            // Manually calculate ApprovalCount for each PR after all PRs are processed
            foreach(var prItem in prList)
            {
                prItem.ApprovalCount = prItem.Approvals.Count(a => a.Approved); // Count approved ones
            }

            var response = new PaginatedPullRequestsResponse
            {
                PullRequests = prList,
                TotalPages = totalPages // Now accessible
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
            public int ApprovalCount { get; set; } // Added for frontend
            public int RequiredApprovals { get; set; } = 1; // Defaulting to 1 for now
            public List<ApprovalDto> Approvals { get; set; } = new(); // Added for frontend

        }

        public class ApprovalDto
        {
            public string DisplayName { get; set; } = string.Empty;
            public string Role { get; set; } = string.Empty;
            public bool Approved { get; set; }
            public DateTime? ApprovedOn { get; set; }
        }

        public class PaginatedPullRequestsResponse
        {
            public List<PullRequestListItemDto> PullRequests { get; set; } = new();
            public int TotalPages { get; set; }
        }
    }
}
