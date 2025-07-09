using System.Collections.Generic;

namespace BB.Web.DTOs;

public class PaginatedCommitsResponse
{
    public List<CommitListItemDto> Commits { get; set; } = new();
    public int TotalPages { get; set; }
    public int PageSize { get; set; }
    public int CurrentPage { get; set; }
    public bool HasNextPage { get; set; }
    public bool HasPreviousPage { get; set; }
} 