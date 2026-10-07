using System;
using System.Collections.Generic;
using System.Linq;

namespace HyPlayer.Features.Downloads.Services;

internal static class DownloadQueueScheduler
{
    internal static DownloadObject[] GetReadyDownloads(IReadOnlyList<DownloadObject> downloads, int maximumCount)
    {
        // Account for occupied slots anywhere in the queue, including tag writes.
        var available = Math.Max(1, maximumCount) - downloads.Count(download =>
            download.Status is DownloadObject.DownloadStatus.Downloading or DownloadObject.DownloadStatus.Processing);
        return downloads.Where(download => download.Status == DownloadObject.DownloadStatus.Queueing)
            .Take(Math.Max(0, available)).ToArray();
    }
}
