using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Common.Net;

namespace Jellyfin.Plugin.Marquee.Sync;

/// <summary>Marquee's Jellyfin API (/c/jf/v1 on the Marquee server).</summary>
public class MarqueeClient
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly IHttpClientFactory _httpClientFactory;

    public MarqueeClient(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    /// <summary>"marquee.example.com" → "https://marquee.example.com", without a trailing slash.</summary>
    public static string NormalizeUrl(string? url)
    {
        var u = (url ?? string.Empty).Trim().TrimEnd('/');
        if (u.Length == 0)
        {
            throw new MarqueeException("Enter the address you open Marquee at.");
        }

        if (!u.Contains("://", StringComparison.Ordinal))
        {
            // A bare home-network address (an IP, a port, a .local or single-label name) is almost
            // always plain http; anything else is a public name behind https.
            var host = u.Split('/')[0];
            var name = host.StartsWith('[') ? host : host.Split(':')[0];
            var local = host.Contains(':', StringComparison.Ordinal) || IPAddress.TryParse(name.Trim('[', ']'), out _)
                || !name.Contains('.', StringComparison.Ordinal) || name.EndsWith(".local", StringComparison.OrdinalIgnoreCase)
                || name.EndsWith(".lan", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".home.arpa", StringComparison.OrdinalIgnoreCase);
            u = (local ? "http://" : "https://") + u;
        }

        if (!Uri.TryCreate(u, UriKind.Absolute, out var parsed) || (parsed.Scheme != "http" && parsed.Scheme != "https"))
        {
            throw new MarqueeException("That isn't a web address.");
        }

        return parsed.GetLeftPart(UriPartial.Path).TrimEnd('/');
    }

    public Task<PairStart> StartPairingAsync(string serverUrl, string name, CancellationToken ct)
        => SendAsync<PairStart>(HttpMethod.Post, serverUrl, "/c/jf/v1/pair/start", null, new { name }, ct);

    public Task<PairPoll> PollPairingAsync(string serverUrl, string poll, CancellationToken ct)
        => SendAsync<PairPoll>(HttpMethod.Get, serverUrl, "/c/jf/v1/pair/poll?id=" + Uri.EscapeDataString(poll), null, null, ct);

    public Task<MarqueeLibrary> GetLibraryAsync(string serverUrl, string key, CancellationToken ct)
        => SendAsync<MarqueeLibrary>(HttpMethod.Get, serverUrl, "/c/jf/v1/library", key, null, ct);

    public Task<JsonElement> ReportProgressAsync(string serverUrl, string key, string mediaKey, double? position, double? duration, bool finished, CancellationToken ct)
        => SendAsync<JsonElement>(HttpMethod.Post, serverUrl, "/c/jf/v1/progress", key, new { key = mediaKey, position, duration, finished }, ct);

    public Task<JsonElement> DisconnectAsync(string serverUrl, string key, CancellationToken ct)
        => SendAsync<JsonElement>(HttpMethod.Post, serverUrl, "/c/jf/v1/disconnect", key, new { }, ct);

    /// <summary>Downloads a file Marquee signed (stream or subtitles) as text.</summary>
    public async Task<string> GetTextAsync(string url, CancellationToken ct)
    {
        using var client = _httpClientFactory.CreateClient(NamedClient.Default);
        using var response = await client.GetAsync(new Uri(url), ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
    }

    private async Task<T> SendAsync<T>(HttpMethod method, string serverUrl, string path, string? key, object? body, CancellationToken ct)
    {
        using var client = _httpClientFactory.CreateClient(NamedClient.Default);
        using var request = new HttpRequestMessage(method, new Uri(serverUrl + path));
        if (!string.IsNullOrEmpty(key))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        }

        if (body is not null)
        {
            request.Content = JsonContent.Create(body, options: Json);
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(60));
        HttpResponseMessage response;
        try
        {
            response = await client.SendAsync(request, timeout.Token).ConfigureAwait(false);
        }
        catch (HttpRequestException e)
        {
            throw new MarqueeException($"Couldn't reach Marquee at {serverUrl} ({e.Message}).");
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new MarqueeException($"Marquee at {serverUrl} didn't answer in time.");
        }

        using (response)
        {
            var text = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                string? message = null;
                try
                {
                    message = JsonSerializer.Deserialize<ErrorBody>(text, Json)?.Error;
                }
                catch (JsonException)
                {
                    // A proxy's error page, not Marquee's.
                }

                if (response.StatusCode == HttpStatusCode.Forbidden || (message is null && response.Headers.Contains("cf-ray")))
                {
                    message ??= "Cloudflare refused the request. Marquee's /c/ path must be bypassed in Cloudflare Access.";
                }

                throw new MarqueeException(message ?? $"Marquee answered {(int)response.StatusCode} {response.ReasonPhrase}.", response.StatusCode);
            }

            try
            {
                return JsonSerializer.Deserialize<T>(text, Json) ?? throw new MarqueeException("Marquee sent an empty answer.");
            }
            catch (JsonException)
            {
                throw new MarqueeException("That address answered, but not like Marquee. Check the address (and that Marquee is up to date).");
            }
        }
    }

    private sealed class ErrorBody
    {
        public string? Error { get; set; }
    }
}

/// <summary>A failure worth showing as it is.</summary>
public class MarqueeException : Exception
{
    public MarqueeException(string message, HttpStatusCode? status = null)
        : base(message)
    {
        Status = status;
    }

    public HttpStatusCode? Status { get; }
}

public class PairStart
{
    public string Code { get; set; } = string.Empty;

    public string Poll { get; set; } = string.Empty;

    public int ExpiresIn { get; set; }
}

public class PairPoll
{
    public string State { get; set; } = string.Empty;

    public string? Key { get; set; }

    public string? Name { get; set; }
}

public class MarqueeLibrary
{
    public MarqueeAccount Account { get; set; } = new();

    public List<MarqueeFilm> Films { get; set; } = [];

    public List<MarqueeShow> Shows { get; set; } = [];
}

public class MarqueeAccount
{
    public string Name { get; set; } = string.Empty;
}

public class MarqueeSubtitle
{
    public string Lang { get; set; } = string.Empty;

    public string Url { get; set; } = string.Empty;
}

public class MarqueeProgress
{
    public double Position { get; set; }

    public double Duration { get; set; }

    public long At { get; set; }
}

public class MarqueeVideo
{
    public string Id { get; set; } = string.Empty;

    /// <summary>Gets or sets Marquee's media key: the same video across MKV → MP4 conversion.</summary>
    public string Key { get; set; } = string.Empty;

    public string File { get; set; } = string.Empty;

    public long Size { get; set; }

    public long Added { get; set; }

    public string Stream { get; set; } = string.Empty;

    public List<MarqueeSubtitle> Subtitles { get; set; } = [];

    public MarqueeProgress? Progress { get; set; }
}

public class MarqueeFilm : MarqueeVideo
{
    public string Title { get; set; } = string.Empty;

    public int? Year { get; set; }

    public long? TmdbId { get; set; }
}

public class MarqueeEpisode : MarqueeVideo
{
    public int Season { get; set; }

    public int Episode { get; set; }

    public int? EpisodeEnd { get; set; }

    public string? Title { get; set; }
}

public class MarqueeShow
{
    public string Id { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public int? Year { get; set; }

    public long? TmdbId { get; set; }

    public List<MarqueeEpisode> Episodes { get; set; } = [];
}
