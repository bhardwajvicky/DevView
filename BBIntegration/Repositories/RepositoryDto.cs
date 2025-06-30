using System;
using System.Text.Json.Serialization;

namespace BBIntegration.Repositories
{
    public class RepositoryDto
    {
        [JsonPropertyName("uuid")]
        public string Uuid { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("full_name")]
        public string FullName { get; set; }

        [JsonPropertyName("workspace")]
        public WorkspaceDto Workspace { get; set; }

        [JsonPropertyName("created_on")]
        public DateTime? CreatedOn { get; set; }
    }

    public class WorkspaceDto
    {
        [JsonPropertyName("slug")]
        public string Slug { get; set; }
    }
} 