using BBIntegration.Commits;
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
        private readonly BitbucketCommitsService _commitsService;

        public SyncController(
            BitbucketUsersService usersService, 
            BitbucketRepositoriesService reposService, 
            BitbucketCommitsService commitsService)
        {
            _usersService = usersService;
            _reposService = reposService;
            _commitsService = commitsService;
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

        [HttpPost("commits/{workspace}/{repoSlug}")]
        public async Task<IActionResult> SyncCommits(string workspace, string repoSlug, [FromBody] DateRangeDto dateRange)
        {
            try
            {
                await _commitsService.SyncCommitsAsync(workspace, repoSlug, dateRange.StartDate, dateRange.EndDate);
                return Ok("Commit synchronization completed successfully.");
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"An error occurred during synchronization: {ex.Message}");
            }
        }
    }

    public class DateRangeDto
    {
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
    }
} 