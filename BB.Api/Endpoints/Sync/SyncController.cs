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

        public SyncController(BitbucketUsersService usersService)
        {
            _usersService = usersService;
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
    }
} 