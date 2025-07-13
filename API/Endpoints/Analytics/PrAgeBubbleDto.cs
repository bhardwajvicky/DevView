using Data.Models;namespace API.Endpoints.Analytics
{
    public class PrAgeBubbleDto
    {
        public int AgeInDays { get; set; }
        public int NumberOfPRs { get; set; }
        public string? RepositoryName { get; set; }
        public string? RepositorySlug { get; set; }
        public string? Workspace { get; set; }
    }
} 