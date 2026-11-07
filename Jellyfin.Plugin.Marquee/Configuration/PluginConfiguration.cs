using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.Marquee.Configuration;

/// <summary>
/// Settings, kept in Jellyfin's plugin configuration folder. The key is a secret: it reads the Marquee
/// library of the person who connected this server.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>Gets or sets the Marquee address, e.g. https://marquee.example.com.</summary>
    public string ServerUrl { get; set; } = string.Empty;

    /// <summary>Gets or sets the key Marquee handed over when the code was entered.</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>Gets or sets the Marquee account's name, for the settings page.</summary>
    public string AccountName { get; set; } = string.Empty;

    /// <summary>Gets or sets where the .strm files go. Empty: Jellyfin's data folder/marquee.</summary>
    public string LibraryPath { get; set; } = string.Empty;

    /// <summary>Gets or sets a value indicating whether to add "Marquee Films" and "Marquee Shows" libraries.</summary>
    public bool CreateLibraries { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether watch progress in Jellyfin is sent to Marquee.</summary>
    public bool ReportProgress { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether subtitles saved in Marquee are copied beside each video.</summary>
    public bool DownloadSubtitles { get; set; } = true;

    /// <summary>Gets or sets how often to sync, in minutes.</summary>
    public int SyncIntervalMinutes { get; set; } = 15;

    /// <summary>Gets or sets when the last sync finished (UTC, ISO 8601).</summary>
    public string LastSync { get; set; } = string.Empty;

    /// <summary>Gets or sets what the last sync did, in a sentence.</summary>
    public string LastSyncSummary { get; set; } = string.Empty;

    /// <summary>Gets or sets why the last sync failed, or empty.</summary>
    public string LastError { get; set; } = string.Empty;
}
