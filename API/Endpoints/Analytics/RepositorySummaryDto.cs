using Data.Models;namespace API.Endpoints.Analytics
{
    public class RepositorySummaryDto
    {
        public required string Name { get; set; }
        public required string Slug { get; set; }
        public required string Workspace { get; set; }
        public DateTime? OldestCommitDate { get; set; }
        public int OpenPullRequestCount { get; set; }
        public DateTime? OldestOpenPullRequestDate { get; set; } // Added for PR Dashboard
        public int PRsMissingApprovalCount { get; set; } // Added for PR Dashboard
    }
} 