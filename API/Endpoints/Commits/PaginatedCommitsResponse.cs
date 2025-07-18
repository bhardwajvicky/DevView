using Data.Models;
using System.Collections.Generic;

namespace API.Endpoints.Commits
{
    public class PaginatedCommitsResponse
    {
        public List<CommitListItemDto> Commits { get; set; } = new();
        public int TotalPages { get; set; }
        public int TotalCommitsCount { get; set; }
        public int AggregatedLinesAdded { get; set; }
        public int AggregatedLinesRemoved { get; set; }
        public int AggregatedCodeLinesAdded { get; set; }
        public int AggregatedCodeLinesRemoved { get; set; }
        public int AggregatedDataLinesAdded { get; set; }
        public int AggregatedDataLinesRemoved { get; set; }
        public int AggregatedConfigLinesAdded { get; set; }
        public int AggregatedConfigLinesRemoved { get; set; }
    }
} 