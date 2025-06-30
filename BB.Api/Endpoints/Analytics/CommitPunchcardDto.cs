namespace BB.Api.Endpoints.Analytics
{
    public class CommitPunchcardDto
    {
        public int DayOfWeek { get; set; }
        public int Hour { get; set; }
        public int CommitCount { get; set; }
    }
} 