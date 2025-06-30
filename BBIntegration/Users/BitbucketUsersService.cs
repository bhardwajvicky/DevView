using System;
using System.Threading.Tasks;
using BBIntegration.Common;

namespace BBIntegration.Users
{
    public class BitbucketUsersService
    {
        private readonly BitbucketApiClient _apiClient;
        private readonly BitbucketConfig _config;

        public BitbucketUsersService(BitbucketConfig config)
        {
            _config = config;
            _apiClient = new BitbucketApiClient(config);
        }

        public async Task SyncUsersAsync(string workspace)
        {
            // Fetch users from Bitbucket
            var usersJson = await _apiClient.GetWorkspaceUsersAsync(workspace);

            // TODO: Parse usersJson and upsert into local DB (Users table)
            // Use _config.DbConnectionString for DB access

            Console.WriteLine("Fetched users from Bitbucket:");
            Console.WriteLine(usersJson);
        }
    }
} 