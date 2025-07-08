namespace BB.Api.Endpoints.Analytics
{
    public class RepositorySummaryDto
    {
        public required string Name { get; set; }
        public required string Slug { get; set; }
        public required string Workspace { get; set; }
        public DateTime? OldestCommitDate { get; set; }
    }
} 