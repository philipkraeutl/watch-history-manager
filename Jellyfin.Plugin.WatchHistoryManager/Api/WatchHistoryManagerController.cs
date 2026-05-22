using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.WatchHistoryManager.Api;

/// <summary>
/// API endpoints for the Watch History Manager plugin.
/// </summary>
[ApiController]
[Authorize]
[Route("WatchHistoryManager")]
public sealed class WatchHistoryManagerController : ControllerBase
{
    /// <summary>
    /// Gets the enabled frontend features.
    /// </summary>
    /// <returns>The frontend feature flags.</returns>
    [HttpGet("Features")]
    public ActionResult<WatchHistoryManagerFeaturesDto> GetFeatures()
    {
        var config = Plugin.Instance?.Configuration;

        return Ok(new WatchHistoryManagerFeaturesDto
        {
            EnableStartPointButton = config?.EnableStartPointButton ?? false,
            IgnoreSpecials = config?.IgnoreSpecials ?? true
        });
    }
}
