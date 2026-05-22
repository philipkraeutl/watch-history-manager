using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.WatchHistoryManager.Configuration;

/// <summary>
/// Plugin configuration.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PluginConfiguration"/> class.
    /// </summary>
    public PluginConfiguration()
    {
        EnableAutoMarkPreviousEpisode = true;
        MinimumWatchedPercentage = 75;
        MaximumRemainingSeconds = 240;
        IgnoreSpecials = true;
        EnableStartPointButton = true;
    }

    /// <summary>
    /// Gets or sets a value indicating whether the automatic previous episode watch-state logic is enabled.
    /// </summary>
    public bool EnableAutoMarkPreviousEpisode { get; set; }

    /// <summary>
    /// Gets or sets the minimum watched percentage required before the previous episode may be marked as watched.
    /// Example: 75 means the episode must be watched to at least 75 percent.
    /// </summary>
    public int MinimumWatchedPercentage { get; set; }

    /// <summary>
    /// Gets or sets the maximum remaining seconds allowed before the previous episode may be marked as watched.
    /// Example: 240 means the episode may be marked as watched if 4 minutes or less are remaining.
    /// </summary>
    public int MaximumRemainingSeconds { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether season 0 / specials should be ignored.
    /// </summary>
    public bool IgnoreSpecials { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the start point button feature is enabled.
    /// </summary>
    public bool EnableStartPointButton { get; set; }
}
