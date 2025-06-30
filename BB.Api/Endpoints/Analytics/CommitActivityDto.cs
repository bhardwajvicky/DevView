using System;

namespace BB.Api.Endpoints.Analytics
{
    public class CommitActivityDto
    {
        public DateTime Date { get; set; }
        public int CommitCount { get; set; }
        public int LinesAdded { get; set; }
        public int LinesRemoved { get; set; }
    }

    public enum GroupingType
    {
        Day,
        Week,
        Month
    }
} 