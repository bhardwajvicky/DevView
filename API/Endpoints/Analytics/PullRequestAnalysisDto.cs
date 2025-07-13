using Data.Models;
using System;
using System.Collections.Generic;

namespace API.Endpoints.Analytics;

public class PullRequestAnalysisDto
{
    public long Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public DateTime CreatedOn { get; set; }
    public DateTime? UpdatedOn { get; set; }
    public DateTime? MergedOn { get; set; }
    public DateTime? ClosedOn { get; set; }
    public UserDto Author { get; set; } = null!;
    public TimeSpan? TimeToMerge { get; set; }
    public List<PullRequestApproverDto> Approvers { get; set; } = new();
}

public class PullRequestApproverDto
{
    public string Uuid { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public bool IsApproved { get; set; }
    public DateTime? ApprovedOn { get; set; }
} 