using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.Lilin.Configuration;

/// <summary>
/// Settings, kept in Jellyfin's plugin configuration folder. The key is a secret: it reads the Lilin
/// library of the person who connected this server.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>Gets or sets the Lilin address, e.g. https://lilin.example.com.</summary>
    public string ServerUrl { get; set; } = string.Empty;

    /// <summary>Gets or sets the key Lilin handed over when the code was entered.</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>Gets or sets the Lilin account's name, for the settings page.</summary>
    public string AccountName { get; set; } = string.Empty;

    /// <summary>Gets or sets where the .strm files go. Empty: Jellyfin's data folder/lilin.</summary>
    public string LibraryPath { get; set; } = string.Empty;

    /// <summary>Gets or sets a value indicating whether to add "Lilin Films" and "Lilin Shows" libraries.</summary>
    public bool CreateLibraries { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether watch progress in Jellyfin is sent to Lilin.</summary>
    public bool ReportProgress { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether subtitles saved in Lilin are copied beside each video.</summary>
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
