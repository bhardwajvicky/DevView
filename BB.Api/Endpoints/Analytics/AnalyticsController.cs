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
            [FromQuery] int repoId,
            [FromQuery] string workspace,
            [FromQuery] DateTime? startDate,
            [FromQuery] DateTime? endDate,
            [FromQuery] GroupingType groupBy = GroupingType.Week)
        {
            if (repoId == 0 && string.IsNullOrEmpty(workspace))
            {
                return BadRequest("The 'workspace' parameter is required when 'repoId' is 0.");
            }

            var result = await _analyticsService.GetCommitActivityAsync(repoId, workspace, startDate, endDate, groupBy);
            return Ok(result);
        }
    }
} 