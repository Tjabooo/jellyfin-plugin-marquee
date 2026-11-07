using System;
using System.Collections.Generic;
using System.IO;
using Jellyfin.Plugin.Marquee.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.Marquee;

/// <summary>
/// Shows a Marquee library in this Jellyfin server: the library is mirrored as .strm files that
/// stream from the Marquee server, and watch progress goes back to Marquee.
/// </summary>
public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;
        DefaultLibraryPath = Path.Combine(applicationPaths.DataPath, "marquee");
    }

    public static Plugin? Instance { get; private set; }

    public override string Name => "Marquee";

    public override string Description => "Watch your Marquee library in Jellyfin.";

    public override Guid Id => Guid.Parse("6d3f8a52-1c4e-4b7a-9f2d-8e5b3c71a904");

    /// <summary>Gets the folder the .strm files go in when none is configured.</summary>
    public string DefaultLibraryPath { get; }

    /// <summary>Gets the folder the .strm files go in.</summary>
    public string LibraryPath => string.IsNullOrWhiteSpace(Configuration.LibraryPath) ? DefaultLibraryPath : Configuration.LibraryPath.Trim();

    public IEnumerable<PluginPageInfo> GetPages()
    {
        return
        [
            new PluginPageInfo
            {
                Name = "Marquee",
                EmbeddedResourcePath = GetType().Namespace + ".Configuration.configPage.html",
                EnableInMainMenu = true,
                DisplayName = "Marquee",
                MenuIcon = "live_tv",
            },
        ];
    }
}
