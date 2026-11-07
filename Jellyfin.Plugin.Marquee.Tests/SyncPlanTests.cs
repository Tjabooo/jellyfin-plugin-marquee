using System.IO;
using System.Linq;
using Jellyfin.Plugin.Marquee.Sync;
using Xunit;

namespace Jellyfin.Plugin.Marquee.Tests;

public class SyncPlanTests
{
    private const string Server = "https://marquee.example.com";

    private static MarqueeLibrary Library() => new()
    {
        Films =
        {
            new MarqueeFilm { Id = "a", Key = "films/heat", Title = "Heat", Year = 1995, TmdbId = 949, Stream = "/c/jf/v1/s/l/sig/a/Heat.mp4", Added = 1,
                Subtitles = { new MarqueeSubtitle { Lang = "sv", Url = "/c/jf/v1/t/l/sig/a/sv.srt" }, new MarqueeSubtitle { Lang = "../x", Url = "/bad" } } },
            new MarqueeFilm { Id = "b", Key = "films/heat-4k", Title = "Heat", Year = 1995, TmdbId = 949, Stream = "/c/jf/v1/s/l/sig/b/Heat.mp4", Added = 2 },
            new MarqueeFilm { Id = "c", Key = "films/what", Title = "What? Why: Now/Then.", Stream = "/c/jf/v1/s/l/sig/c/x.mp4" },
        },
        Shows =
        {
            new MarqueeShow
            {
                Id = "s", Title = "The Bear", Year = 2022, TmdbId = 136315,
                Episodes =
                {
                    new MarqueeEpisode { Id = "e2", Key = "tv/bear/s01e02", Season = 1, Episode = 2, EpisodeEnd = 3, Stream = "/c/jf/v1/s/l/sig/e2/x.mp4" },
                    new MarqueeEpisode { Id = "e0", Key = "tv/bear/s00e01", Season = 0, Episode = 1, Stream = "/c/jf/v1/s/l/sig/e0/x.mp4" },
                },
            },
        },
    };

    [Fact]
    public void FilmsAndEpisodesGetJellyfinNames()
    {
        var plan = SyncPlan.Build(Library(), Server).ToDictionary(p => p.MediaKey);
        Assert.Equal(Path.Combine("Films", "Heat (1995) [tmdbid-949]", "Heat (1995).strm"), plan["films/heat"].RelativePath);
        Assert.Equal(Path.Combine("Films", "Heat (1995) [tmdbid-949]", "Heat (1995) - 2.strm"), plan["films/heat-4k"].RelativePath);
        Assert.Equal(Path.Combine("Films", "What Why Now Then", "What Why Now Then.strm"), plan["films/what"].RelativePath);
        Assert.Equal(Path.Combine("Shows", "The Bear (2022) [tmdbid-136315]", "Season 01", "The Bear S01E02-E03.strm"), plan["tv/bear/s01e02"].RelativePath);
        Assert.Equal(Path.Combine("Shows", "The Bear (2022) [tmdbid-136315]", "Specials", "The Bear S00E01.strm"), plan["tv/bear/s00e01"].RelativePath);
        Assert.Equal(Server + "/c/jf/v1/s/l/sig/a/Heat.mp4", plan["films/heat"].Url);
    }

    [Fact]
    public void OnlySafeSubtitleLanguagesAreKept()
    {
        var heat = SyncPlan.Build(Library(), Server).Single(p => p.MediaKey == "films/heat");
        var sub = Assert.Single(heat.Subtitles);
        Assert.Equal(Path.Combine("Films", "Heat (1995) [tmdbid-949]", "Heat (1995).sv.srt"), sub.RelativePath);
        Assert.Equal(Server + "/c/jf/v1/t/l/sig/a/sv.srt", sub.Url);
    }

    [Theory]
    [InlineData("CON", "_CON")]
    [InlineData("  a\tb  ", "a b")]
    [InlineData("...", "Untitled")]
    [InlineData("Amélie", "Amélie")]
    public void NamesAreSafeOnEveryFileSystem(string name, string expected) => Assert.Equal(expected, SyncPlan.CleanName(name));

    [Theory]
    [InlineData("marquee.example.com", "https://marquee.example.com")]
    [InlineData("https://marquee.example.com/", "https://marquee.example.com")]
    [InlineData("192.168.1.10:8080", "http://192.168.1.10:8080")]
    [InlineData("mediabox:8080", "http://mediabox:8080")]
    [InlineData("media.local", "http://media.local")]
    [InlineData("http://10.0.0.5:8080/", "http://10.0.0.5:8080")]
    public void AddressesAreNormalized(string input, string expected) => Assert.Equal(expected, MarqueeClient.NormalizeUrl(input));

    [Theory]
    [InlineData("")]
    [InlineData("ftp://x")]
    public void BadAddressesAreRefused(string input) => Assert.Throws<MarqueeException>(() => MarqueeClient.NormalizeUrl(input));
}
