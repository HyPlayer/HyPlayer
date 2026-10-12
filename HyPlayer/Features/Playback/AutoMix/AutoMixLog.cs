using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage;

namespace HyPlayer.Features.Playback.AutoMix;

public static class AutoMixLog
{
    private static readonly ConcurrentQueue<string> Pending = new();
    private static int _flushing;

    public static void Write(string message)
    {
        Pending.Enqueue($"{DateTime.Now:HH:mm:ss.fff} {message}");
        if (Interlocked.Exchange(ref _flushing, 1) == 1) return;
        _ = Task.Run(FlushAsync);
    }

    private static async Task FlushAsync()
    {
        try
        {
            await Task.Delay(200);
            var file = await ApplicationData.Current.LocalCacheFolder.CreateFileAsync("automix.log", CreationCollisionOption.OpenIfExists);
            var properties = await file.GetBasicPropertiesAsync();
            if (properties.Size > 512 * 1024)
                file = await ApplicationData.Current.LocalCacheFolder.CreateFileAsync("automix.log", CreationCollisionOption.ReplaceExisting);
            var lines = new System.Text.StringBuilder();
            while (Pending.TryDequeue(out var line)) lines.AppendLine(line);
            if (lines.Length > 0) await FileIO.AppendTextAsync(file, lines.ToString());
        }
        catch
        {
        }
        finally
        {
            Interlocked.Exchange(ref _flushing, 0);
            if (!Pending.IsEmpty && Interlocked.Exchange(ref _flushing, 1) == 0) _ = Task.Run(FlushAsync);
        }
    }
}
