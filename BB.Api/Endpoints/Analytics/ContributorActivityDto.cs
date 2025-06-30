using System;

namespace BB.Api.Endpoints.Analytics
{
    public class ContributorActivityDto
    {
        public DateTime Date { get; set; }
        public int UserId { get; set; }
        public string DisplayName { get; set; }
        public int CommitCount { get; set; }
        public int TotalLinesAdded { get; set; }
        public int TotalLinesRemoved { get; set; }
        public int CodeLinesAdded { get; set; }
        public int CodeLinesRemoved { get; set; }
    }
} 