using Microsoft.AspNetCore.Mvc;
using Vidar.Host.Persistence;

namespace Vidar.Host.Api;

[ApiController]
[Route("api/threshold-events")]
public sealed class ThresholdEventsController : ControllerBase
{
    private readonly IThresholdEventLogRepository _eventLogRepo;

    public ThresholdEventsController(IThresholdEventLogRepository eventLogRepo)
    {
        _eventLogRepo = eventLogRepo;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] int skip = 0, [FromQuery] int take = 50)
    {
        var events = await _eventLogRepo.GetRecentAsync(skip, take);
        var total = await _eventLogRepo.CountAsync();
        return Ok(new { items = events, totalCount = total });
    }
}
