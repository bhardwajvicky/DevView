using BB.Api.Services;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Threading.Tasks;

namespace BB.Api.Endpoints.Analytics
{
    [ApiController]
    [Route("api/[controller]")]
    public class AnalyticsController : ControllerBase
    {
        private readonly AnalyticsService _analyticsService;

        public AnalyticsController(AnalyticsService analyticsService)
        {
            _analyticsService = analyticsService;
        }

        [HttpGet("commits/activity")]
        public async Task<IActionResult> GetCommitActivity(
            [FromQuery] string repoSlug,
            [FromQuery] string workspace,
            [FromQuery] DateTime? startDate,
            [FromQuery] DateTime? endDate,
            [FromQuery] GroupingType groupBy = GroupingType.Day)
        {
            if (string.IsNullOrEmpty(repoSlug) && string.IsNullOrEmpty(workspace))
            {
                return BadRequest("Either 'repoSlug' or 'workspace' must be provided.");
            }

            var result = await _analyticsService.GetCommitActivityAsync(repoSlug, workspace, startDate, endDate, groupBy);
            return Ok(result);
        }
    }
} 