using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Jellyfin.Plugin.Lilin.Sync;

/// <summary>One .strm file to have on disk, with the subtitles beside it.</summary>
public sealed record PlannedVideo(string RelativePath, string Url, string MediaKey, long AddedMs, IReadOnlyList<PlannedSubtitle> Subtitles);

/// <summary>A subtitle file to have beside a .strm file.</summary>
public sealed record PlannedSubtitle(string RelativePath, string Url);

/// <summary>
/// Turns Lilin's library into Jellyfin's naming: Films/Title (Year) [tmdbid-1]/Title (Year).strm and
/// Shows/Show (Year) [tmdbid-2]/Season 01/Show S01E02.strm. No disk access, so it's easy to test.
/// </summary>
public static partial class SyncPlan
{
    public const string FilmsFolder = "Films";
    public const string ShowsFolder = "Shows";

    public static List<PlannedVideo> Build(LilinLibrary library, string serverUrl)
    {
        var plan = new List<PlannedVideo>();
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var film in library.Films.OrderBy(f => f.Added).ThenBy(f => f.Id, StringComparer.Ordinal))
        {
            var name = CleanName(film.Title) + (film.Year is int y ? $" ({y})" : string.Empty);
            var folder = name + (film.TmdbId is long id ? $" [tmdbid-{id}]" : string.Empty);
            var rel = Unique(Path.Combine(FilmsFolder, folder, name), taken);
            plan.Add(Video(rel, film, serverUrl));
        }

        foreach (var show in library.Shows)
        {
            var name = CleanName(show.Title);
            var folder = name + (show.Year is int y ? $" ({y})" : string.Empty) + (show.TmdbId is long id ? $" [tmdbid-{id}]" : string.Empty);
            foreach (var ep in show.Episodes.OrderBy(e => e.Season).ThenBy(e => e.Episode).ThenBy(e => e.Added))
            {
                var code = string.Create(CultureInfo.InvariantCulture, $"S{ep.Season:00}E{ep.Episode:00}")
                    + (ep.EpisodeEnd is int end && end > ep.Episode ? string.Create(CultureInfo.InvariantCulture, $"-E{end:00}") : string.Empty);
                var season = ep.Season == 0 ? "Specials" : string.Create(CultureInfo.InvariantCulture, $"Season {ep.Season:00}");
                var rel = Unique(Path.Combine(ShowsFolder, folder, season, $"{name} {code}"), taken);
                plan.Add(Video(rel, ep, serverUrl));
            }
        }

        return plan;
    }

    /// <summary>A file or folder name every file system accepts.</summary>
    public static string CleanName(string? name)
    {
        var sb = new StringBuilder();
        foreach (var c in (name ?? string.Empty).Normalize(NormalizationForm.FormC))
        {
            sb.Append(c < 32 || "<>:\"/\\|?*".Contains(c, StringComparison.Ordinal) ? ' ' : c);
        }

        var clean = Spaces().Replace(sb.ToString(), " ").Trim().TrimEnd('.').Trim();
        if (clean.Length > 100)
        {
            clean = clean[..100].TrimEnd('.', ' ');
        }

        // Reserved device names on Windows.
        if (Reserved().IsMatch(clean))
        {
            clean = "_" + clean;
        }

        return clean.Length == 0 ? "Untitled" : clean;
    }

    // Same name twice (two releases of a film, a repeated episode): Jellyfin's "Name - 2" version naming.
    private static string Unique(string stem, HashSet<string> taken)
    {
        var rel = stem + ".strm";
        for (var n = 2; !taken.Add(rel); n++)
        {
            rel = stem + string.Create(CultureInfo.InvariantCulture, $" - {n}.strm");
        }

        return rel;
    }

    private static PlannedVideo Video(string rel, LilinVideo v, string serverUrl)
    {
        var stem = rel[..^".strm".Length];
        var subs = v.Subtitles
            .Where(s => LangCode().IsMatch(s.Lang) && s.Url.StartsWith('/'))
            .GroupBy(s => s.Lang, StringComparer.OrdinalIgnoreCase)
            .Select(g => new PlannedSubtitle($"{stem}.{g.Key.ToLowerInvariant()}.srt", serverUrl + g.First().Url))
            .ToList();
        if (!v.Stream.StartsWith('/'))
        {
            throw new LilinException("Lilin sent a stream link this plugin doesn't understand. Update the plugin.");
        }

        return new PlannedVideo(rel, serverUrl + v.Stream, v.Key, v.Added, subs);
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();

    [GeneratedRegex(@"^(CON|PRN|AUX|NUL|COM\d|LPT\d)(\.|$)", RegexOptions.IgnoreCase)]
    private static partial Regex Reserved();

    [GeneratedRegex("^[a-z]{2,3}$", RegexOptions.IgnoreCase)]
    private static partial Regex LangCode();
}
