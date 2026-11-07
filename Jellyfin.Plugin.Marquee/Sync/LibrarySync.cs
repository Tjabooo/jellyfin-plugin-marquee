using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Marquee.Sync;

/// <summary>
/// Mirrors the Marquee library into the plugin's folder: writes .strm files and subtitles, removes what
/// left Marquee, and tells Jellyfin what changed. Only files this plugin wrote (listed in
/// .marquee-sync.json) are ever deleted.
/// </summary>
public class LibrarySync
{
    private const string ManifestName = ".marquee-sync.json";
    private static readonly TimeSpan FreshSubtitles = TimeSpan.FromDays(2);
    private readonly MarqueeClient _client;
    private readonly ILibraryManager _libraryManager;
    private readonly ILibraryMonitor _libraryMonitor;
    private readonly ILogger<LibrarySync> _logger;
    private readonly SemaphoreSlim _running = new(1, 1);
    private Dictionary<string, string> _keysByPath = new(StringComparer.OrdinalIgnoreCase);

    public LibrarySync(MarqueeClient client, ILibraryManager libraryManager, ILibraryMonitor libraryMonitor, ILogger<LibrarySync> logger)
    {
        _client = client;
        _libraryManager = libraryManager;
        _libraryMonitor = libraryMonitor;
        _logger = logger;
    }

    private static Plugin Plugin => Plugin.Instance ?? throw new InvalidOperationException("The Marquee plugin isn't loaded.");

    /// <summary>Marquee's media key for a .strm file this plugin wrote, or null.</summary>
    public string? MediaKeyFor(string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return null;
        }

        if (_keysByPath.Count == 0)
        {
            LoadIndex();
        }

        return _keysByPath.TryGetValue(path, out var key) ? key : null;
    }

    public async Task RunAsync(IProgress<double>? progress, CancellationToken ct)
    {
        var config = Plugin.Configuration;
        if (string.IsNullOrEmpty(config.Key) || string.IsNullOrEmpty(config.ServerUrl))
        {
            _logger.LogDebug("Marquee isn't connected; nothing to sync");
            return;
        }

        if (!await _running.WaitAsync(0, ct).ConfigureAwait(false))
        {
            _logger.LogInformation("A Marquee sync is already running");
            return;
        }

        try
        {
            var library = await _client.GetLibraryAsync(config.ServerUrl, config.Key, ct).ConfigureAwait(false);
            progress?.Report(10);
            var summary = await ApplyAsync(library, config.ServerUrl, config.DownloadSubtitles, progress, ct).ConfigureAwait(false);
            config.AccountName = library.Account.Name;
            config.LastError = string.Empty;
            config.LastSyncSummary = summary;
            _logger.LogInformation("Marquee sync: {Summary}", summary);
        }
        catch (MarqueeException e)
        {
            config.LastError = e.Message;
            if (e.Status == System.Net.HttpStatusCode.Unauthorized)
            {
                // Disconnected in Marquee: the .strm links no longer work either, so they go.
                config.Key = string.Empty;
                config.LastError = "Marquee disconnected this server. Connect again to see the library.";
                RemoveAll();
            }

            _logger.LogWarning("Marquee sync failed: {Message}", e.Message);
            throw;
        }
        finally
        {
            config.LastSync = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
            Plugin.SaveConfiguration();
            _running.Release();
        }
    }

    /// <summary>Deletes every file this plugin wrote (after disconnecting).</summary>
    public void RemoveAll()
    {
        var root = Plugin.LibraryPath;
        var manifest = ReadManifest(root);
        var changed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rel in manifest.Files.Keys.Concat(manifest.Subtitles))
        {
            DeleteOwned(root, rel, changed);
        }

        WriteManifest(root, new Manifest());
        _keysByPath = new(StringComparer.OrdinalIgnoreCase);
        Notify(changed);
    }

    private async Task<string> ApplyAsync(MarqueeLibrary library, string serverUrl, bool subtitles, IProgress<double>? progress, CancellationToken ct)
    {
        var root = Plugin.LibraryPath;
        var plan = SyncPlan.Build(library, serverUrl);
        var old = ReadManifest(root);
        var next = new Manifest();
        var changed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int added = 0, updated = 0, removed = 0, subs = 0;
        Directory.CreateDirectory(Path.Combine(root, SyncPlan.FilmsFolder));
        Directory.CreateDirectory(Path.Combine(root, SyncPlan.ShowsFolder));

        for (var i = 0; i < plan.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var video = plan[i];
            var full = Path.Combine(root, video.RelativePath);
            var content = video.Url + "\n";
            var existing = File.Exists(full) ? await File.ReadAllTextAsync(full, ct).ConfigureAwait(false) : null;
            if (existing != content)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(full)!);
                await File.WriteAllTextAsync(full, content, new UTF8Encoding(false), ct).ConfigureAwait(false);
                if (existing is null)
                {
                    added++;
                }
                else
                {
                    updated++;
                }

                changed.Add(Path.GetDirectoryName(full)!);
            }

            next.Files[video.RelativePath] = video.MediaKey;
            if (subtitles)
            {
                var fresh = DateTimeOffset.UtcNow - DateTimeOffset.FromUnixTimeMilliseconds(video.AddedMs) < FreshSubtitles;
                foreach (var sub in video.Subtitles)
                {
                    next.Subtitles.Add(sub.RelativePath);
                    var subPath = Path.Combine(root, sub.RelativePath);
                    // New videos get their subtitles again for a while: Marquee fixes their timing after download.
                    var stale = !File.Exists(subPath) || (fresh && DateTime.UtcNow - File.GetLastWriteTimeUtc(subPath) > TimeSpan.FromHours(3));
                    if (!stale)
                    {
                        continue;
                    }

                    try
                    {
                        var text = await _client.GetTextAsync(sub.Url, ct).ConfigureAwait(false);
                        var had = File.Exists(subPath) ? await File.ReadAllTextAsync(subPath, ct).ConfigureAwait(false) : null;
                        if (had != text)
                        {
                            await File.WriteAllTextAsync(subPath, text, new UTF8Encoding(false), ct).ConfigureAwait(false);
                            changed.Add(Path.GetDirectoryName(subPath)!);
                            subs++;
                        }
                        else
                        {
                            File.SetLastWriteTimeUtc(subPath, DateTime.UtcNow);
                        }
                    }
                    catch (Exception e) when (e is System.Net.Http.HttpRequestException or IOException)
                    {
                        _logger.LogDebug(e, "Couldn't fetch subtitles {Path}", sub.RelativePath);
                    }
                }
            }

            progress?.Report(10 + (80.0 * (i + 1) / Math.Max(plan.Count, 1)));
        }

        foreach (var rel in old.Files.Keys.Where(k => !next.Files.ContainsKey(k)))
        {
            DeleteOwned(root, rel, changed);
            removed++;
        }

        foreach (var rel in old.Subtitles.Where(s => !next.Subtitles.Contains(s)))
        {
            DeleteOwned(root, rel, changed);
        }

        WriteManifest(root, next);
        _keysByPath = IndexOf(root, next);
        progress?.Report(95);

        var created = Plugin.Configuration.CreateLibraries && await EnsureLibrariesAsync(root).ConfigureAwait(false);
        if (!created)
        {
            Notify(changed);
        }

        progress?.Report(100);
        var parts = new List<string>
        {
            $"{Count(library.Films.Count, "film")} and {Count(library.Shows.Sum(s => s.Episodes.Count), "episode")}",
        };
        if (added + updated + removed + subs == 0)
        {
            parts.Add("nothing new");
        }
        else
        {
            if (added > 0)
            {
                parts.Add($"{added} added");
            }

            if (updated > 0)
            {
                parts.Add($"{updated} updated");
            }

            if (removed > 0)
            {
                parts.Add($"{removed} removed");
            }

            if (subs > 0)
            {
                parts.Add(Count(subs, "subtitle file"));
            }
        }

        return string.Join(", ", parts);
    }

    private static string Count(int n, string what) => n == 1 ? $"1 {what}" : $"{n} {what}s";

    // "Marquee Films" and "Marquee Shows", unless some library already covers those folders.
    private async Task<bool> EnsureLibrariesAsync(string root)
    {
        var folders = _libraryManager.GetVirtualFolders();
        var made = false;
        foreach (var (name, sub, type) in new[]
        {
            ("Marquee Films", SyncPlan.FilmsFolder, CollectionTypeOptions.movies),
            ("Marquee Shows", SyncPlan.ShowsFolder, CollectionTypeOptions.tvshows),
        })
        {
            var path = Path.Combine(root, sub);
            if (folders.Any(f => f.Locations.Any(l => SamePath(l, path))))
            {
                continue;
            }

            var unique = name;
            for (var n = 2; folders.Any(f => string.Equals(f.Name, unique, StringComparison.OrdinalIgnoreCase)); n++)
            {
                unique = $"{name} {n}";
            }

            _logger.LogInformation("Adding the {Name} library for {Path}", unique, path);
            var options = new LibraryOptions
            {
                PathInfos = [new MediaPathInfo(path)],
                EnableRealtimeMonitor = false,
                SaveLocalMetadata = false,
            };
            await _libraryManager.AddVirtualFolder(unique, type, options, true).ConfigureAwait(false);
            made = true;
        }

        return made;
    }

    private void Notify(HashSet<string> changed)
    {
        if (changed.Count == 0)
        {
            return;
        }

        if (changed.Count > 100)
        {
            _libraryManager.QueueLibraryScan();
            return;
        }

        foreach (var dir in changed)
        {
            // A folder that's gone is reported through its nearest parent that still exists.
            var d = dir;
            while (!Directory.Exists(d) && Path.GetDirectoryName(d) is string parent)
            {
                d = parent;
            }

            _libraryMonitor.ReportFileSystemChanged(d);
        }
    }

    private static void DeleteOwned(string root, string rel, HashSet<string> changed)
    {
        var full = Path.GetFullPath(Path.Combine(root, rel));
        var rootFull = Path.GetFullPath(root);
        if (!full.StartsWith(rootFull + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (File.Exists(full))
        {
            File.Delete(full);
        }

        // Empty folders go too, up to (not including) Films/ and Shows/.
        var dir = Path.GetDirectoryName(full);
        changed.Add(dir!);
        while (dir is not null && dir.Length > rootFull.Length
            && !SamePath(dir, Path.Combine(rootFull, SyncPlan.FilmsFolder)) && !SamePath(dir, Path.Combine(rootFull, SyncPlan.ShowsFolder))
            && Directory.Exists(dir) && !Directory.EnumerateFileSystemEntries(dir).Any())
        {
            Directory.Delete(dir);
            dir = Path.GetDirectoryName(dir);
        }
    }

    internal static bool SamePath(string a, string b)
        => string.Equals(Path.GetFullPath(a).TrimEnd(Path.DirectorySeparatorChar), Path.GetFullPath(b).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);

    private void LoadIndex()
    {
        try
        {
            var root = Plugin.LibraryPath;
            _keysByPath = IndexOf(root, ReadManifest(root));
        }
        catch (InvalidOperationException)
        {
            // Not loaded yet.
        }
    }

    private static Dictionary<string, string> IndexOf(string root, Manifest manifest)
        => manifest.Files.ToDictionary(kv => Path.GetFullPath(Path.Combine(root, kv.Key)), kv => kv.Value, StringComparer.OrdinalIgnoreCase);

    private static Manifest ReadManifest(string root)
    {
        try
        {
            var path = Path.Combine(root, ManifestName);
            return File.Exists(path) ? JsonSerializer.Deserialize<Manifest>(File.ReadAllText(path)) ?? new Manifest() : new Manifest();
        }
        catch (JsonException)
        {
            return new Manifest();
        }
    }

    private static void WriteManifest(string root, Manifest manifest)
    {
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, ManifestName);
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(manifest));
        File.Move(temp, path, true);
    }

    private sealed class Manifest
    {
        public int Version { get; set; } = 1;

        public Dictionary<string, string> Files { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        public HashSet<string> Subtitles { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }
}
