using BBIntegration.Common;
using BBIntegration.Users; // Reusing PaginatedResponseDto
using Dapper;
using Microsoft.Data.SqlClient;
using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace BBIntegration.Repositories
{
    public class BitbucketRepositoriesService
    {
        private readonly BitbucketApiClient _apiClient;
        private readonly BitbucketConfig _config;

        public BitbucketRepositoriesService(BitbucketConfig config, BitbucketApiClient apiClient)
        {
            _config = config;
            _apiClient = apiClient;
        }

        public async Task SyncRepositoriesAsync(string workspace)
        {
            var reposJson = await _apiClient.GetWorkspaceRepositoriesAsync(workspace);

            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var pagedResponse = JsonSerializer.Deserialize<PaginatedResponseDto<RepositoryDto>>(reposJson, options);

            if (pagedResponse?.Values == null || !pagedResponse.Values.Any())
            {
                Console.WriteLine("No repositories found to sync.");
                return;
            }

            using var connection = new SqlConnection(_config.DbConnectionString);
            await connection.OpenAsync();

            foreach (var repo in pagedResponse.Values)
            {
                const string sql = @"
                    MERGE Repositories AS target
                    USING (SELECT @Uuid AS BitbucketRepoId, @Slug AS Slug, @Name AS Name, @FullName AS FullName, @Workspace AS Workspace, @CreatedOn AS CreatedOn) AS source
                    ON (target.BitbucketRepoId = source.BitbucketRepoId)
                    WHEN MATCHED THEN 
                        UPDATE SET Name = source.Name, FullName = source.FullName, Workspace = source.Workspace, Slug = source.Slug
                    WHEN NOT MATCHED THEN
                        INSERT (BitbucketRepoId, Slug, Name, FullName, Workspace, CreatedOn)
                        VALUES (source.BitbucketRepoId, source.Slug, source.Name, source.FullName, source.Workspace, source.CreatedOn);
                ";
                await connection.ExecuteAsync(sql, new
                {
                    repo.Uuid,
                    repo.Slug,
                    repo.Name,
                    repo.FullName,
                    Workspace = workspace,
                    repo.CreatedOn
                });
            }

            Console.WriteLine($"{pagedResponse.Values.Count()} repositories successfully synced.");
        }
    }
} 