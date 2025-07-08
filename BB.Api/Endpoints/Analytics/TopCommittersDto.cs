using System;
using System.Collections.Generic;

namespace BB.Api.Endpoints.Analytics
{
    public class TopCommittersDto
    {
        public int UserId { get; set; }
        public string DisplayName { get; set; } = string.Empty;
        public string AvatarUrl { get; set; } = string.Empty;
        public int TotalCommits { get; set; }
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
        public List<ContributorActivityDto> ActivityData { get; set; } = new List<ContributorActivityDto>();
    }

    public class TopCommittersResponseDto
    {
        public List<TopCommittersDto> TopCommitters { get; set; } = new List<TopCommittersDto>();
        public List<TopCommittersDto> BottomCommitters { get; set; } = new List<TopCommittersDto>();
    }
} 