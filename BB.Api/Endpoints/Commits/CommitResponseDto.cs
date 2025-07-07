using System;

namespace BB.Api.Endpoints.Commits
{
    public class CommitResponseDto
    {
        public int Id { get; set; }
        public string BitbucketCommitHash { get; set; } = string.Empty;
        public int RepositoryId { get; set; }
        public string RepositoryName { get; set; } = string.Empty;
        public string RepositorySlug { get; set; } = string.Empty;
        public int AuthorId { get; set; }
        public string AuthorDisplayName { get; set; } = string.Empty;
        public string AuthorUsername { get; set; } = string.Empty;
        public DateTime Date { get; set; }
        public string? Message { get; set; }
        
        // Legacy line counts
        public int LinesAdded { get; set; }
        public int LinesRemoved { get; set; }
        
        // Code-specific line counts
        public int? CodeLinesAdded { get; set; }
        public int? CodeLinesRemoved { get; set; }
        
        // File type-specific line counts (new classification system)
        public int DataLinesAdded { get; set; }
        public int DataLinesRemoved { get; set; }
        public int ConfigLinesAdded { get; set; }
        public int ConfigLinesRemoved { get; set; }
        public int DocsLinesAdded { get; set; }
        public int DocsLinesRemoved { get; set; }
        
        public bool IsMerge { get; set; }
        public bool IsPRMergeCommit { get; set; }
    }
}
