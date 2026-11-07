using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Model.Tasks;

namespace Jellyfin.Plugin.Marquee.Sync;

/// <summary>Dashboard → Scheduled Tasks → Marquee → Sync Marquee library.</summary>
public class SyncTask : IScheduledTask
{
    private readonly LibrarySync _sync;

    public SyncTask(LibrarySync sync)
    {
        _sync = sync;
    }

    public string Name => "Sync Marquee library";

    public string Key => "MarqueeLibrarySync";

    public string Description => "Brings new films and episodes from Marquee into Jellyfin and removes ones that left.";

    public string Category => "Marquee";

    public Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken) => _sync.RunAsync(progress, cancellationToken);

    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        var minutes = Math.Clamp(Plugin.Instance?.Configuration.SyncIntervalMinutes ?? 15, 5, 1440);
        yield return new TaskTriggerInfo { Type = TaskTriggerInfoType.StartupTrigger };
        yield return new TaskTriggerInfo { Type = TaskTriggerInfoType.IntervalTrigger, IntervalTicks = TimeSpan.FromMinutes(minutes).Ticks };
    }
}
