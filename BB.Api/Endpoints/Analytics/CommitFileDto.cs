using System.Text.Json.Serialization;

namespace BB.Api.Endpoints.Analytics
{
    public class CommitFileDto
    {
        public int Id { get; set; }
        public string? FilePath { get; set; }
        public string FileType { get; set; } = string.Empty;
        public int LinesAdded { get; set; }
        public int LinesRemoved { get; set; }
        public bool ExcludeFromReporting { get; set; }
        public string CommitHash { get; set; } = string.Empty;

        [JsonIgnore]
        public int DiffLineId { get; set; }
    }

    public class CommitFileUpdateDto
    {
        public int FileId { get; set; }
        public string PropertyName { get; set; } = string.Empty;
        public bool Value { get; set; }
    }
} 