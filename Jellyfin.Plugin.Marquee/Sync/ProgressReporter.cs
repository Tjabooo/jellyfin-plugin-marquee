using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Session;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Marquee.Sync;

/// <summary>
/// Sends what's watched in Jellyfin back to Marquee, so Continue watching there picks up where Jellyfin
/// stopped: every 30 seconds while playing, at stop, and "finished" when Jellyfin counts it as played.
/// </summary>
public sealed class ProgressReporter : IHostedService, IDisposable
{
    private static readonly TimeSpan Every = TimeSpan.FromSeconds(30);
    private readonly ISessionManager _sessions;
    private readonly LibrarySync _sync;
    private readonly MarqueeClient _client;
    private readonly ILogger<ProgressReporter> _logger;
    private readonly ConcurrentDictionary<string, DateTime> _lastSent = new();
    private readonly CancellationTokenSource _stopping = new();

    public ProgressReporter(ISessionManager sessions, LibrarySync sync, MarqueeClient client, ILogger<ProgressReporter> logger)
    {
        _sessions = sessions;
        _sync = sync;
        _client = client;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _sessions.PlaybackProgress += OnProgress;
        _sessions.PlaybackStopped += OnStopped;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _sessions.PlaybackProgress -= OnProgress;
        _sessions.PlaybackStopped -= OnStopped;
        _stopping.Cancel();
        return Task.CompletedTask;
    }

    public void Dispose() => _stopping.Dispose();

    private void OnProgress(object? sender, PlaybackProgressEventArgs e)
    {
        var key = KeyOf(e);
        if (key is null || e.IsPaused)
        {
            return;
        }

        var now = DateTime.UtcNow;
        if (_lastSent.TryGetValue(key, out var last) && now - last < Every)
        {
            return;
        }

        _lastSent[key] = now;
        Send(key, e, false);
    }

    private void OnStopped(object? sender, PlaybackStopEventArgs e)
    {
        var key = KeyOf(e);
        if (key is null)
        {
            return;
        }

        _lastSent.TryRemove(key, out _);
        Send(key, e, e.PlayedToCompletion);
    }

    private string? KeyOf(PlaybackProgressEventArgs e)
    {
        var config = Plugin.Instance?.Configuration;
        if (config is null || !config.ReportProgress || string.IsNullOrEmpty(config.Key) || e.Item is null)
        {
            return null;
        }

        return _sync.MediaKeyFor(e.Item.Path);
    }

    private void Send(string key, PlaybackProgressEventArgs e, bool finished)
    {
        var config = Plugin.Instance!.Configuration;
        var position = (e.PlaybackPositionTicks ?? 0) / (double)TimeSpan.TicksPerSecond;
        var duration = (e.Item.RunTimeTicks ?? 0) / (double)TimeSpan.TicksPerSecond;
        // Without a known length Marquee can't place the position; at the very start there's nothing to keep.
        if (!finished && (duration <= 0 || position < 5))
        {
            return;
        }

        var server = config.ServerUrl;
        var secret = config.Key;
        _ = Task.Run(
            async () =>
            {
                try
                {
                    await _client.ReportProgressAsync(server, secret, key, finished ? null : position, finished ? null : duration, finished, _stopping.Token).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is MarqueeException or OperationCanceledException)
                {
                    _logger.LogDebug("Couldn't send progress to Marquee: {Message}", ex.Message);
                }
            },
            _stopping.Token);
    }
}
