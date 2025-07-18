using Data.Models;namespace API.Endpoints.Analytics
{
    public class FileClassificationSummaryDto
    {
        public int TotalCommits { get; set; }
        public int TotalLinesAdded { get; set; }
        public int TotalLinesRemoved { get; set; }
        
        // Code files
        public int CodeLinesAdded { get; set; }
        public int CodeLinesRemoved { get; set; }
        public int CodeCommits { get; set; }
        
        // Data files
        public int DataLinesAdded { get; set; }
        public int DataLinesRemoved { get; set; }
        public int DataCommits { get; set; }
        
        // Config files
        public int ConfigLinesAdded { get; set; }
        public int ConfigLinesRemoved { get; set; }
        public int ConfigCommits { get; set; }
        
        // Documentation files
        public int DocsLinesAdded { get; set; }
        public int DocsLinesRemoved { get; set; }
        public int DocsCommits { get; set; }
    }
} 