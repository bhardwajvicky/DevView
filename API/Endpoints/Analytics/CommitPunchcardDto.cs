using Data.Models;namespace API.Endpoints.Analytics
{
    public class CommitPunchcardDto
    {
        public int DayOfWeek { get; set; } // 0=Sunday, 1=Monday, ..., 6=Saturday
        public int HourOfDay { get; set; } // 0-23
        public int CommitCount { get; set; }
    }
} 