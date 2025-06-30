using System;

namespace BBIntegration.Common
{
    public class BitbucketConfig
    {
        // Database connection string, populated from configuration
        public string DbConnectionString { get; set; }

        // Bitbucket API base URL, populated from configuration
        public string BitbucketApiBaseUrl { get; set; }

        // Bitbucket OAuth Consumer Key and Secret
        public string BitbucketConsumerKey { get; set; }
        public string BitbucketConsumerSecret { get; set; }
    }
}
