using Microsoft.Extensions.Configuration;

namespace BBIntegration.Common
{
    public class BitbucketConfig
    {
        // Database connection string, populated from configuration
        public string DbConnectionString { get; set; }

        // Bitbucket API base URL, populated from configuration
        [ConfigurationKeyName("Bitbucket:ApiBaseUrl")]
        public string BitbucketApiBaseUrl { get; set; }

        // Bitbucket OAuth Consumer Key and Secret
        [ConfigurationKeyName("Bitbucket:ConsumerKey")]
        public string BitbucketConsumerKey { get; set; }
        [ConfigurationKeyName("Bitbucket:ConsumerSecret")]
        public string BitbucketConsumerSecret { get; set; }
    }
}
