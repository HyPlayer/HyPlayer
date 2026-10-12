#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.Search;
using HyPlayer.Application;
using HyPlayer.Domain.Settings;
using HyPlayer.NeteaseProvider.LocalMusic;
using HyPlayer.Platform.Storage.Audio;

namespace HyPlayer.Platform.Downloads;

public static class DownloadedTrackIndex
{
    private const string IndexFileName = "downloaded_tracks.tsv";
    private static readonly string[] _audioExtensions = [".flac", ".mp3", ".m4a", ".ogg", ".wav", ".ape"];
    private static readonly ConcurrentDictionary<string, string> _entries = new();
    private static readonly SemaphoreSlim _gate = new(1, 1);
    private static Task? _loading;
    private static int _scanScheduled;

    private static Task EnsureLoadedAsync()
    {
        return _loading ??= LoadAsync();
    }

    private static async Task LoadAsync()
    {
        try
        {
            if (await ApplicationData.Current.LocalFolder.TryGetItemAsync(IndexFileName) is not StorageFile file)
                return;

            foreach (var line in await FileIO.ReadLinesAsync(file))
            {
                var tab = line.IndexOf('\t');
                if (tab <= 0) continue;
                _entries[line[..tab]] = line[(tab + 1)..];
            }
        }
        catch
        {
        }
    }

    private static async Task SaveAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var builder = new StringBuilder();
            foreach (var (id, path) in _entries)
                builder.Append(id).Append('\t').Append(path).Append('\n');
            var file = await ApplicationData.Current.LocalFolder.CreateFileAsync(
                IndexFileName,
                CreationCollisionOption.ReplaceExisting);
            await FileIO.WriteTextAsync(file, builder.ToString());
        }
        catch
        {
        }
        finally
        {
            _gate.Release();
        }
    }

    public static async Task RecordAsync(string? songId, IStorageFile? file)
    {
        if (string.IsNullOrEmpty(songId) || file is null) return;
        await EnsureLoadedAsync().ConfigureAwait(false);
        _entries[songId] = file.Path;
        await SaveAsync().ConfigureAwait(false);
    }

    public static async Task<StorageFile?> TryGetAsync(string? songId)
    {
        if (string.IsNullOrEmpty(songId)) return null;
        if (!AppDepository.Resolve<DownloadSettings>().PreferDownloadedFile) return null;
        await EnsureLoadedAsync().ConfigureAwait(false);
        if (!_entries.TryGetValue(songId, out var path)) return null;
        try
        {
            var file = await StorageFile.GetFileFromPathAsync(path);
            if ((await file.GetBasicPropertiesAsync()).Size > 0) return file;
        }
        catch
        {
        }

        if (_entries.TryRemove(songId, out _)) _ = SaveAsync();
        return null;
    }

    public static void ScheduleScan(TimeSpan delay)
    {
        if (Interlocked.Exchange(ref _scanScheduled, 1) == 1) return;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(delay).ConfigureAwait(false);
                await ScanAsync().ConfigureAwait(false);
            }
            catch
            {
            }
            finally
            {
                Volatile.Write(ref _scanScheduled, 0);
            }
        });
    }

    private static async Task ScanAsync()
    {
        await EnsureLoadedAsync().ConfigureAwait(false);
        var downloadDirectory = await AppDepository.Resolve<DownloadSettings>()
            .GetDownloadDirectoryAsync()
            .ConfigureAwait(false);
        var searchDirectory = AppDepository.Resolve<LocalLibrarySettings>().SearchDirectory;
        var known = new HashSet<string>(_entries.Values, StringComparer.OrdinalIgnoreCase);
        var changed = false;
        foreach (var path in new[] { downloadDirectory, searchDirectory }.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (string.IsNullOrEmpty(path)) continue;
            IReadOnlyList<StorageFile> files;
            try
            {
                var folder = await StorageFolder.GetFolderFromPathAsync(path);
                var options = new QueryOptions(CommonFileQuery.DefaultQuery, _audioExtensions)
                {
                    FolderDepth = FolderDepth.Deep
                };
                files = await folder.CreateFileQueryWithOptions(options).GetFilesAsync();
            }
            catch
            {
                continue;
            }

            foreach (var file in files)
            {
                if (!known.Add(file.Path)) continue;
                var id = await ReadTrackIdAsync(file).ConfigureAwait(false);
                if (id is null) continue;
                _entries[id] = file.Path;
                changed = true;
            }
        }

        if (changed) await SaveAsync().ConfigureAwait(false);
    }

    private static async Task<string?> ReadTrackIdAsync(StorageFile file)
    {
        try
        {
            using var abstraction = await UwpStorageFileAbstraction.OpenAsync(file);
            using var tagFile = TagLibHelper.Create(abstraction, file.FileType);
            return The163KeyHelper.TryGetTrackId(tagFile.Tag, out var id) ? id.ToString() : null;
        }
        catch
        {
            return null;
        }
    }
}
