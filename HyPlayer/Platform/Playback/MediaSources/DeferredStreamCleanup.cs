using System;
using System.Threading.Tasks;

namespace HyPlayer.Platform.Playback.MediaSources;

/// <summary>Transfers disposal to the completed decoder or waits before releasing its input.</summary>
internal static class DeferredStreamCleanup
{
    public static async Task ReleaseAsync<TStream, TSource>(Task<TStream> opening, Task? probing, Task<TSource?>? creating)
        where TStream : IDisposable
        where TSource : class, IDisposable
    {
        TStream stream;
        try { stream = await opening.ConfigureAwait(false); }
        catch { return; } // Opening failed and transferred no resource.
        if (probing is not null)
        {
            try { await probing.ConfigureAwait(false); }
            catch { /* Fall back after the failed probe has stopped using the stream. */ }
        }
        if (creating is not null)
        {
            TSource? source = null;
            try { source = await creating.ConfigureAwait(false); }
            catch { /* A failed creation leaves ownership with the caller. */ }
            if (source is not null)
            {
                source.Dispose(); // The successfully created source owns its input stream.
                return;
            }
        }
        stream.Dispose();
    }
}
