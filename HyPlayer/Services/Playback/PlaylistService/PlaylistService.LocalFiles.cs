using System.Collections.Generic;
using System.Threading.Tasks;
using Windows.Storage;

namespace HyPlayer.Services.Playback.PlaylistService;

public sealed partial class PlaylistService
{
    // ────────────── 本地文件追加（占位） ──────────────

    /// <inheritdoc />
    public Task AppendStorageFilesAsync(IEnumerable<StorageFile> files)
    {
        // Local file loading is handled by PickLocalFileAsync() or ILocalFileImportService.
        // This legacy interface slot is kept for API symmetry with older append paths.
        return Task.CompletedTask;
    }
}
