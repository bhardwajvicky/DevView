using Data.Models;
using System;

namespace API.Endpoints.Analytics
{
    public class CommitActivityDto
    {
        public DateTime Date { get; set; }
        public int CommitCount { get; set; }
        public int TotalLinesAdded { get; set; }
        public int TotalLinesRemoved { get; set; }
        public int CodeLinesAdded { get; set; }
        public int CodeLinesRemoved { get; set; }
        public int DataLinesAdded { get; set; }
        public int DataLinesRemoved { get; set; }
        public int ConfigLinesAdded { get; set; }
        public int ConfigLinesRemoved { get; set; }
        public int DocsLinesAdded { get; set; }
        public int DocsLinesRemoved { get; set; }
        public bool IsMergeCommit { get; set; }
    }

    public enum GroupingType
    {
        Day,
        Week,
        Month
    }
} 