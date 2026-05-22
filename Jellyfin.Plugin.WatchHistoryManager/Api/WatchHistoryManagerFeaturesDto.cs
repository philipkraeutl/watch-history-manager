namespace Jellyfin.Plugin.WatchHistoryManager.Api;

/// <summary>
/// Feature flags exposed to the Jellyfin Web helper script.
/// </summary>
public sealed class WatchHistoryManagerFeaturesDto
{
    /// <summary>
    /// Gets or sets a value indicating whether the start point button should be shown.
    /// </summary>
    public bool EnableStartPointButton { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether specials / season 0 should be ignored.
    /// </summary>
    public bool IgnoreSpecials { get; set; }
}
