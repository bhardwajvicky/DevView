using Data.Models;
using System;

namespace API.Endpoints.Analytics
{
    public class ContributorActivityDto
    {
        public int UserId { get; set; }
        public required string DisplayName { get; set; }
        public required string AvatarUrl { get; set; }
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
    }
} 