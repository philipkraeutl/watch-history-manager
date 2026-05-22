using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Database.Implementations.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.WatchHistoryManager.Services;

/// <summary>
/// Monitors playback events and marks the previous episode as watched
/// when the next episode of the same series starts and the configured
/// thresholds are reached.
/// </summary>
internal sealed class PlaybackMonitorService : IHostedService
{
    private const long TicksPerSecond = 10_000_000L;

    private readonly ISessionManager _sessionManager;
    private readonly IUserDataManager _userDataManager;
    private readonly ILogger<PlaybackMonitorService> _logger;

    private readonly ConcurrentDictionary<string, EpisodeProgressSnapshot> _lastEpisodeByUserSession = new();

    public PlaybackMonitorService(
        ISessionManager sessionManager,
        IUserDataManager userDataManager,
        ILogger<PlaybackMonitorService> logger)
    {
        _sessionManager = sessionManager;
        _userDataManager = userDataManager;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _sessionManager.PlaybackStart += OnPlaybackStart;
        _sessionManager.PlaybackProgress += OnPlaybackProgress;

        _logger.LogInformation("Watch History Manager playback monitor started.");

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _sessionManager.PlaybackStart -= OnPlaybackStart;
        _sessionManager.PlaybackProgress -= OnPlaybackProgress;

        _logger.LogInformation("Watch History Manager playback monitor stopped.");

        return Task.CompletedTask;
    }

    private void OnPlaybackStart(object? sender, PlaybackProgressEventArgs eventArgs)
    {
        HandlePlaybackEvent(eventArgs, "start");
    }

    private void OnPlaybackProgress(object? sender, PlaybackProgressEventArgs eventArgs)
    {
        HandlePlaybackEvent(eventArgs, "progress");
    }

    private void HandlePlaybackEvent(PlaybackProgressEventArgs eventArgs, string eventType)
    {
        try
        {
            var config = Plugin.Instance?.Configuration;

            if (config is null || !config.EnableAutoMarkPreviousEpisode)
            {
                return;
            }

            if (eventArgs.Item is not Episode currentEpisode)
            {
                return;
            }

            if (eventArgs.Users is null || eventArgs.Users.Count == 0)
            {
                return;
            }

            foreach (var user in eventArgs.Users)
            {
                var key = BuildSessionKey(user, eventArgs);

                if (_lastEpisodeByUserSession.TryGetValue(key, out var previousSnapshot)
                    && previousSnapshot.Item.Id != currentEpisode.Id)
                {
                    TryMarkPreviousEpisodeAsWatched(user, previousSnapshot, currentEpisode, config);
                }

                _lastEpisodeByUserSession[key] = CreateSnapshot(currentEpisode, eventArgs);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Watch History Manager failed while handling playback {EventType}.", eventType);
        }
    }

    private void TryMarkPreviousEpisodeAsWatched(
    User user,
    EpisodeProgressSnapshot previous,
    Episode currentEpisode,
    Configuration.PluginConfiguration config)
{
    if (!IsDirectNextEpisode(previous.Item, currentEpisode, config.IgnoreSpecials))
    {
        return;
    }

    if (!ThresholdReached(previous, config))
    {
        return;
    }

    previous.Item.MarkPlayed(user, DateTime.UtcNow, true);

    _logger.LogInformation(
        "Marked previous episode as watched for user {UserId}: {SeriesName} S{SeasonNumber:00}E{EpisodeNumber:00}, watched {WatchedPercentage:0.00}%, remaining {RemainingSeconds:0}s.",
        user.Id,
        previous.Item.SeriesName,
        previous.Item.ParentIndexNumber,
        previous.Item.IndexNumber,
        previous.WatchedPercentage,
        previous.RemainingSeconds);
}

    private static bool IsDirectNextEpisode(Episode previousEpisode, Episode currentEpisode, bool ignoreSpecials)
    {
        if (ignoreSpecials
            && (previousEpisode.ParentIndexNumber == 0 || currentEpisode.ParentIndexNumber == 0))
        {
            return false;
        }

        if (previousEpisode.SeriesId == Guid.Empty || currentEpisode.SeriesId == Guid.Empty)
        {
            return false;
        }

        if (previousEpisode.SeriesId != currentEpisode.SeriesId)
        {
            return false;
        }

        if (previousEpisode.ParentIndexNumber != currentEpisode.ParentIndexNumber)
        {
            return false;
        }

        if (!previousEpisode.IndexNumber.HasValue || !currentEpisode.IndexNumber.HasValue)
        {
            return false;
        }

        var previousEpisodeEndNumber = previousEpisode.IndexNumberEnd ?? previousEpisode.IndexNumber.Value;

        return currentEpisode.IndexNumber.Value == previousEpisodeEndNumber + 1;
    }

    private static bool ThresholdReached(
        EpisodeProgressSnapshot previous,
        Configuration.PluginConfiguration config)
    {
        var minimumPercentage = Math.Clamp(config.MinimumWatchedPercentage, 0, 100);
        var maximumRemainingSeconds = Math.Max(config.MaximumRemainingSeconds, 0);

        var watchedPercentageReached = previous.WatchedPercentage >= minimumPercentage;
        var remainingSecondsReached = previous.RemainingSeconds <= maximumRemainingSeconds;

        return watchedPercentageReached || remainingSecondsReached;
    }

    private static EpisodeProgressSnapshot CreateSnapshot(Episode episode, PlaybackProgressEventArgs eventArgs)
    {
        var runtimeTicks = episode.RunTimeTicks ?? 0;
        var positionTicks = eventArgs.PlaybackPositionTicks ?? 0;

        if (runtimeTicks <= 0)
        {
            return new EpisodeProgressSnapshot(
                episode,
                positionTicks,
                runtimeTicks,
                0,
                double.MaxValue);
        }

        var watchedPercentage = Math.Clamp((double)positionTicks / runtimeTicks * 100, 0, 100);
        var remainingTicks = Math.Max(runtimeTicks - positionTicks, 0);
        var remainingSeconds = remainingTicks / (double)TicksPerSecond;

        return new EpisodeProgressSnapshot(
            episode,
            positionTicks,
            runtimeTicks,
            watchedPercentage,
            remainingSeconds);
    }

    private static string BuildSessionKey(User user, PlaybackProgressEventArgs eventArgs)
    {
        var sessionId = eventArgs.Session?.Id;

        if (!string.IsNullOrWhiteSpace(sessionId))
        {
            return $"{user.Id}:{sessionId}";
        }

        if (!string.IsNullOrWhiteSpace(eventArgs.DeviceId))
        {
            return $"{user.Id}:{eventArgs.DeviceId}";
        }

        return $"{user.Id}:unknown";
    }

    private sealed record EpisodeProgressSnapshot(
        Episode Item,
        long PositionTicks,
        long RuntimeTicks,
        double WatchedPercentage,
        double RemainingSeconds);
}
