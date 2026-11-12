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

namespace Jellyfin.Plugin.Lilin.Sync;

/// <summary>Lilin's Jellyfin API (/c/jf/v1 on the Lilin server).</summary>
public class LilinClient
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly IHttpClientFactory _httpClientFactory;

    public LilinClient(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    /// <summary>"lilin.example.com" → "https://lilin.example.com", without a trailing slash.</summary>
    public static string NormalizeUrl(string? url)
    {
        var u = (url ?? string.Empty).Trim().TrimEnd('/');
        if (u.Length == 0)
        {
            throw new LilinException("Enter the address you open Lilin at.");
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
            throw new LilinException("That isn't a web address.");
        }

        return parsed.GetLeftPart(UriPartial.Path).TrimEnd('/');
    }

    public Task<PairStart> StartPairingAsync(string serverUrl, string name, CancellationToken ct)
        => SendAsync<PairStart>(HttpMethod.Post, serverUrl, "/c/jf/v1/pair/start", null, new { name }, ct);

    public Task<PairPoll> PollPairingAsync(string serverUrl, string poll, CancellationToken ct)
        => SendAsync<PairPoll>(HttpMethod.Get, serverUrl, "/c/jf/v1/pair/poll?id=" + Uri.EscapeDataString(poll), null, null, ct);

    public Task<LilinLibrary> GetLibraryAsync(string serverUrl, string key, CancellationToken ct)
        => SendAsync<LilinLibrary>(HttpMethod.Get, serverUrl, "/c/jf/v1/library", key, null, ct);

    public Task<JsonElement> ReportProgressAsync(string serverUrl, string key, string mediaKey, double? position, double? duration, bool finished, CancellationToken ct)
        => SendAsync<JsonElement>(HttpMethod.Post, serverUrl, "/c/jf/v1/progress", key, new { key = mediaKey, position, duration, finished }, ct);

    public Task<JsonElement> DisconnectAsync(string serverUrl, string key, CancellationToken ct)
        => SendAsync<JsonElement>(HttpMethod.Post, serverUrl, "/c/jf/v1/disconnect", key, new { }, ct);

    /// <summary>Downloads a file Lilin signed (stream or subtitles) as text.</summary>
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
            throw new LilinException($"Couldn't reach Lilin at {serverUrl} ({e.Message}).");
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new LilinException($"Lilin at {serverUrl} didn't answer in time.");
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
                    // A proxy's error page, not Lilin's.
                }

                if (response.StatusCode == HttpStatusCode.Forbidden || (message is null && response.Headers.Contains("cf-ray")))
                {
                    message ??= "Cloudflare refused the request. Lilin's /c/ path must be bypassed in Cloudflare Access.";
                }

                throw new LilinException(message ?? $"Lilin answered {(int)response.StatusCode} {response.ReasonPhrase}.", response.StatusCode);
            }

            try
            {
                return JsonSerializer.Deserialize<T>(text, Json) ?? throw new LilinException("Lilin sent an empty answer.");
            }
            catch (JsonException)
            {
                throw new LilinException("That address answered, but not like Lilin. Check the address (and that Lilin is up to date).");
            }
        }
    }

    private sealed class ErrorBody
    {
        public string? Error { get; set; }
    }
}

/// <summary>A failure worth showing as it is.</summary>
public class LilinException : Exception
{
    public LilinException(string message, HttpStatusCode? status = null)
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

public class LilinLibrary
{
    public LilinAccount Account { get; set; } = new();

    public List<LilinFilm> Films { get; set; } = [];

    public List<LilinShow> Shows { get; set; } = [];
}

public class LilinAccount
{
    public string Name { get; set; } = string.Empty;
}

public class LilinSubtitle
{
    public string Lang { get; set; } = string.Empty;

    public string Url { get; set; } = string.Empty;
}

public class LilinProgress
{
    public double Position { get; set; }

    public double Duration { get; set; }

    public long At { get; set; }
}

public class LilinVideo
{
    public string Id { get; set; } = string.Empty;

    /// <summary>Gets or sets Lilin's media key: the same video across MKV → MP4 conversion.</summary>
    public string Key { get; set; } = string.Empty;

    public string File { get; set; } = string.Empty;

    public long Size { get; set; }

    public long Added { get; set; }

    public string Stream { get; set; } = string.Empty;

    public List<LilinSubtitle> Subtitles { get; set; } = [];

    public LilinProgress? Progress { get; set; }
}

public class LilinFilm : LilinVideo
{
    public string Title { get; set; } = string.Empty;

    public int? Year { get; set; }

    public long? TmdbId { get; set; }
}

public class LilinEpisode : LilinVideo
{
    public int Season { get; set; }

    public int Episode { get; set; }

    public int? EpisodeEnd { get; set; }

    public string? Title { get; set; }
}

public class LilinShow
{
    public string Id { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public int? Year { get; set; }

    public long? TmdbId { get; set; }

    public List<LilinEpisode> Episodes { get; set; } = [];
}
