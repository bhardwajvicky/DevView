using BBIntegration.Commits; // Reusing CommitDto and BitbucketUserDto
using System;
using System.Text.Json.Serialization;

namespace BBIntegration.PullRequests
{
    public class PullRequestDto
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("title")]
        public string Title { get; set; }

        [JsonPropertyName("state")]
        public string State { get; set; }

        [JsonPropertyName("author")]
        public CommitAuthorDto Author { get; set; }

        [JsonPropertyName("created_on")]
        public DateTime CreatedOn { get; set; }

        [JsonPropertyName("updated_on")]
        public DateTime? UpdatedOn { get; set; }

        [JsonPropertyName("merge_commit")]
        public CommitDto MergeCommit { get; set; }
    }
}
