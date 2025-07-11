using System;
using System.Collections.Generic;

namespace BB.Api.Endpoints.Commits
{
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
} 