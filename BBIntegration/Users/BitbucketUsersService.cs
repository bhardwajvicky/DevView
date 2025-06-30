using BBIntegration.Common;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace BBIntegration.Users
{
    public class BitbucketUsersService
    {
        private readonly BitbucketApiClient _apiClient;
        private readonly BitbucketConfig _config;
        private readonly ILogger<BitbucketUsersService> _logger;

        public BitbucketUsersService(BitbucketConfig config, BitbucketApiClient apiClient, ILogger<BitbucketUsersService> logger)
        {
            _config = config;
            _apiClient = apiClient;
            _logger = logger;
        }

        public async Task SyncUsersAsync(string workspace)
        {
            _logger.LogInformation("Starting user sync for workspace: {Workspace}", workspace);
            
            string nextPageUrl = null;
            do
            {
                // 1. Fetch users from Bitbucket
                var usersJson = await _apiClient.GetWorkspaceUsersAsync(workspace);

                // 2. Deserialize the JSON into our DTOs
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var pagedResponse = JsonSerializer.Deserialize<PaginatedResponseDto<WorkspaceMembershipDto>>(usersJson, options);

                if (pagedResponse?.Values == null || !pagedResponse.Values.Any())
                {
                    Console.WriteLine("No users found to sync.");
                    return;
                }

                // 3. Connect to the database and perform the upsert
                using var connection = new SqlConnection(_config.DbConnectionString);
                await connection.OpenAsync();

                var usersToSync = pagedResponse.Values.Select(m => m.User);

                // 4. Use a MERGE statement for an efficient upsert operation
                const string sql = @"
                    MERGE INTO Users AS Target
                    USING (SELECT @Uuid AS BitbucketUserId, @DisplayName AS DisplayName, @AvatarUrl AS AvatarUrl, @CreatedOn AS CreatedOn) AS Source
                    ON Target.BitbucketUserId = Source.BitbucketUserId
                    WHEN MATCHED THEN
                        UPDATE SET 
                            DisplayName = Source.DisplayName,
                            AvatarUrl = Source.AvatarUrl,
                            CreatedOn = Source.CreatedOn
                    WHEN NOT MATCHED BY TARGET THEN
                        INSERT (BitbucketUserId, DisplayName, AvatarUrl, CreatedOn)
                        VALUES (Source.BitbucketUserId, Source.DisplayName, Source.AvatarUrl, Source.CreatedOn);
                ";

                foreach (var user in usersToSync)
                {
                    if (user == null || string.IsNullOrEmpty(user.Uuid)) continue;
                    
                    if (string.IsNullOrEmpty(user.DisplayName))
                    {
                        _logger.LogWarning("User with UUID {UserUuid} has a null or empty DisplayName. Skipping.", user.Uuid);
                        continue;
                    }

                    await connection.ExecuteAsync(sql, new
                    {
                        Uuid = user.Uuid,
                        DisplayName = user.DisplayName,
                        AvatarUrl = user.Links?.Avatar?.Href,
                        CreatedOn = user.CreatedOn
                    });
                }

                Console.WriteLine($"{usersToSync.Count()} users successfully synced.");

                // TODO: Implement handling for pagination ('next' URL in pagedResponse)
            } while (!string.IsNullOrEmpty(nextPageUrl));
            
            _logger.LogInformation("User sync completed for workspace: {Workspace}", workspace);
        }
    }
} 