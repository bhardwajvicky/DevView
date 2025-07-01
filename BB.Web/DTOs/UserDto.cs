using System;

namespace BB.Web.DTOs
{
    public class UserDto
    {
        public int Id { get; set; }
        public string BitbucketUserId { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string AvatarUrl { get; set; } = string.Empty;
        public DateTime? CreatedOn { get; set; }
    }
} 