using BBIntegration.Repositories;
using BBIntegration.Users;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Threading.Tasks;

namespace BB.Api.Endpoints.Sync
{
    [ApiController]
    [Route("api/[controller]")]
    public class SyncController : ControllerBase
    {
        private readonly BitbucketUsersService _usersService;
        private readonly BitbucketRepositoriesService _reposService;

        public SyncController(BitbucketUsersService usersService, BitbucketRepositoriesService reposService)
        {
            _usersService = usersService;
            _reposService = reposService;
        }

        [HttpPost("users/{workspace}")]
        public async Task<IActionResult> SyncUsers(string workspace)
        {
            try
            {
                await _usersService.SyncUsersAsync(workspace);
                return Ok("User synchronization completed successfully.");
            }
            catch (Exception ex)
            {
                // In a real app, you would log the exception
                return StatusCode(500, $"An error occurred during synchronization: {ex.Message}");
            }
        }

        [HttpPost("repositories/{workspace}")]
        public async Task<IActionResult> SyncRepositories(string workspace)
        {
            try
            {
                await _reposService.SyncRepositoriesAsync(workspace);
                return Ok("Repository synchronization completed successfully.");
            }
            catch (Exception ex)
            {
                // In a real app, you would log the exception
                return StatusCode(500, $"An error occurred during synchronization: {ex.Message}");
            }
        }
    }
} 