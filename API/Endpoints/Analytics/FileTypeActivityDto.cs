using System;

namespace API.Endpoints.Analytics
{
    public class FileTypeActivityDto
    {
        public DateTime Date { get; set; }
        public string FileType { get; set; } = string.Empty; // "code", "data", "config", "docs"
        public int CommitCount { get; set; }
        public int LinesAdded { get; set; }
        public int LinesRemoved { get; set; }
        public int NetLinesChanged { get; set; }
    }
} 