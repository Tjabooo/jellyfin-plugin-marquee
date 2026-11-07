using System;
using System.Collections.Concurrent;
using System.Net.Mime;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Marquee.Sync;
using MediaBrowser.Controller;
using MediaBrowser.Model.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.Marquee.Api;

/// <summary>The plugin's settings page talks to Marquee through these (a browser can't reach Marquee's API directly).</summary>
[ApiController]
[Route("Marquee")]
[Authorize(Policy = "RequiresElevation")]
[Produces(MediaTypeNames.Application.Json)]
public class MarqueeController : ControllerBase
{
    // Pairing in progress: poll id → Marquee address.
    private static readonly ConcurrentDictionary<string, (string Url, DateTime Started)> Pending = new();
    private readonly MarqueeClient _client;
    private readonly LibrarySync _sync;
    private readonly ITaskManager _tasks;
    private readonly IServerApplicationHost _host;

    public MarqueeController(MarqueeClient client, LibrarySync sync, ITaskManager tasks, IServerApplicationHost host)
    {
        _client = client;
        _sync = sync;
        _tasks = tasks;
        _host = host;
    }

    private static Plugin Plugin => Plugin.Instance!;

    [HttpGet("Status")]
    public ActionResult<object> Status()
    {
        var c = Plugin.Configuration;
        var task = Array.Find([.. _tasks.ScheduledTasks], t => t.ScheduledTask is SyncTask);
        return new
        {
            connected = !string.IsNullOrEmpty(c.Key),
            serverUrl = c.ServerUrl,
            account = c.AccountName,
            libraryPath = Plugin.LibraryPath,
            defaultLibraryPath = Plugin.DefaultLibraryPath,
            customLibraryPath = c.LibraryPath,
            createLibraries = c.CreateLibraries,
            reportProgress = c.ReportProgress,
            downloadSubtitles = c.DownloadSubtitles,
            syncIntervalMinutes = c.SyncIntervalMinutes,
            lastSync = c.LastSync,
            lastSyncSummary = c.LastSyncSummary,
            lastError = c.LastError,
            syncing = task?.State == TaskState.Running,
            serverName = _host.FriendlyName,
        };
    }

    public class PairRequest
    {
        public string ServerUrl { get; set; } = string.Empty;
    }

    [HttpPost("Pair/Start")]
    public async Task<ActionResult<object>> StartPairing([FromBody] PairRequest body, CancellationToken ct)
    {
        try
        {
            var url = MarqueeClient.NormalizeUrl(body.ServerUrl);
            var name = string.IsNullOrWhiteSpace(_host.FriendlyName) ? "Jellyfin" : _host.FriendlyName + " (Jellyfin)";
            var started = await _client.StartPairingAsync(url, name, ct).ConfigureAwait(false);
            foreach (var old in Pending)
            {
                if (DateTime.UtcNow - old.Value.Started > TimeSpan.FromMinutes(15))
                {
                    Pending.TryRemove(old.Key, out _);
                }
            }

            Pending[started.Poll] = (url, DateTime.UtcNow);
            return new { code = started.Code, poll = started.Poll, expiresIn = started.ExpiresIn, serverUrl = url };
        }
        catch (MarqueeException e)
        {
            return BadRequest(new { error = e.Message });
        }
    }

    [HttpGet("Pair/Poll")]
    public async Task<ActionResult<object>> PollPairing([FromQuery] string id, CancellationToken ct)
    {
        if (!Pending.TryGetValue(id ?? string.Empty, out var pending))
        {
            return new { state = "expired" };
        }

        try
        {
            var r = await _client.PollPairingAsync(pending.Url, id!, ct).ConfigureAwait(false);
            if (r.State == "approved" && !string.IsNullOrEmpty(r.Key))
            {
                Pending.TryRemove(id!, out _);
                var c = Plugin.Configuration;
                c.ServerUrl = pending.Url;
                c.Key = r.Key;
                c.LastError = string.Empty;
                Plugin.SaveConfiguration();
                _tasks.QueueScheduledTask<SyncTask>();
                return new { state = "approved" };
            }

            if (r.State != "waiting")
            {
                Pending.TryRemove(id!, out _);
            }

            return new { state = r.State };
        }
        catch (MarqueeException e)
        {
            return BadRequest(new { error = e.Message });
        }
    }

    public class SettingsRequest
    {
        public string? LibraryPath { get; set; }

        public bool CreateLibraries { get; set; } = true;

        public bool ReportProgress { get; set; } = true;

        public bool DownloadSubtitles { get; set; } = true;

        public int SyncIntervalMinutes { get; set; } = 15;
    }

    [HttpPost("Settings")]
    public ActionResult Settings([FromBody] SettingsRequest body)
    {
        var c = Plugin.Configuration;
        var path = (body.LibraryPath ?? string.Empty).Trim();
        if (path.Length > 0 && !System.IO.Path.IsPathFullyQualified(path))
        {
            return BadRequest(new { error = "The folder must be a full path, like /media/marquee or D:\\Marquee." });
        }

        // Compare the folders, not the text: "/x/marquee/" (or the default typed out) is the same folder,
        // and treating it as a move would delete the library it is about to keep using.
        var newFolder = path.Length == 0 ? Plugin.DefaultLibraryPath : path;
        if (LibrarySync.SamePath(newFolder, Plugin.DefaultLibraryPath))
        {
            path = string.Empty;
        }

        var moved = !LibrarySync.SamePath(newFolder, Plugin.LibraryPath) && !string.IsNullOrEmpty(c.Key);
        if (moved)
        {
            _sync.RemoveAll(); // the old folder's files; the sync below writes the new one
        }

        c.LibraryPath = path;
        c.CreateLibraries = body.CreateLibraries;
        c.ReportProgress = body.ReportProgress;
        c.DownloadSubtitles = body.DownloadSubtitles;
        var minutes = Math.Clamp(body.SyncIntervalMinutes, 5, 1440);
        if (minutes != c.SyncIntervalMinutes)
        {
            c.SyncIntervalMinutes = minutes;
            var worker = Array.Find([.. _tasks.ScheduledTasks], t => t.ScheduledTask is SyncTask);
            if (worker is not null)
            {
                worker.Triggers = [.. worker.ScheduledTask.GetDefaultTriggers()];
            }
        }

        Plugin.SaveConfiguration();
        if (moved)
        {
            _tasks.QueueScheduledTask<SyncTask>();
        }

        return NoContent();
    }

    [HttpPost("Sync")]
    public ActionResult Sync()
    {
        if (string.IsNullOrEmpty(Plugin.Configuration.Key))
        {
            return BadRequest(new { error = "Connect to Marquee first." });
        }

        _tasks.QueueScheduledTask<SyncTask>();
        return NoContent();
    }

    [HttpPost("Disconnect")]
    public async Task<ActionResult> Disconnect(CancellationToken ct)
    {
        var c = Plugin.Configuration;
        if (!string.IsNullOrEmpty(c.Key))
        {
            try
            {
                await _client.DisconnectAsync(c.ServerUrl, c.Key, ct).ConfigureAwait(false);
            }
            catch (MarqueeException)
            {
                // Already disconnected there, or unreachable: forget the key here all the same.
            }
        }

        _sync.RemoveAll();
        c.Key = string.Empty;
        c.AccountName = string.Empty;
        c.LastSyncSummary = string.Empty;
        c.LastError = string.Empty;
        Plugin.SaveConfiguration();
        return NoContent();
    }
}
