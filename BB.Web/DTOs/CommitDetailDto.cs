using System;

namespace BB.Web.DTOs
{
    public class CommitDetailDto
    {
        public int Id { get; set; }
        public string CommitHash { get; set; } = string.Empty;
        public DateTime Date { get; set; }
        public string Message { get; set; } = string.Empty;
        public string AuthorName { get; set; } = string.Empty;
        public string RepositoryName { get; set; } = string.Empty;
        public string RepositorySlug { get; set; } = string.Empty;
        public int LinesAdded { get; set; }
        public int LinesRemoved { get; set; }
        public int CodeLinesAdded { get; set; }
        public int CodeLinesRemoved { get; set; }
        public bool IsMerge { get; set; }
    }
} 