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

            const string sql = @"
                MERGE INTO Repositories AS Target
                USING (SELECT @Uuid AS BitbucketRepoId, @Name AS Name, @FullName AS FullName, @Workspace AS Workspace, @CreatedOn AS CreatedOn) AS Source
                ON Target.BitbucketRepoId = Source.BitbucketRepoId
                WHEN MATCHED THEN
                    UPDATE SET 
                        Name = Source.Name,
                        FullName = Source.FullName,
                        Workspace = Source.Workspace,
                        CreatedOn = Source.CreatedOn
                WHEN NOT MATCHED BY TARGET THEN
                    INSERT (BitbucketRepoId, Name, FullName, Workspace, CreatedOn)
                    VALUES (Source.BitbucketRepoId, Source.Name, Source.FullName, Source.Workspace, Source.CreatedOn);
            ";

            foreach (var repo in pagedResponse.Values)
            {
                await connection.ExecuteAsync(sql, new
                {
                    Uuid = repo.Uuid,
                    Name = repo.Name,
                    FullName = repo.FullName,
                    Workspace = repo.Workspace?.Slug,
                    CreatedOn = repo.CreatedOn
                });
            }

            Console.WriteLine($"{pagedResponse.Values.Count()} repositories successfully synced.");
        }
    }
} 