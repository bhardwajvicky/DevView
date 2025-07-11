using System.Collections.Generic;

namespace BB.Api.Endpoints.Commits
{
    public class PaginatedCommitsResponse
    {
        public List<CommitListItemDto> Commits { get; set; } = new();
        public int TotalPages { get; set; }
    }
} 