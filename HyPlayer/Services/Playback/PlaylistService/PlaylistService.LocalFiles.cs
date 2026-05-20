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
        // 本地文件加载逻辑由 MediaProvider 层处理，此处仅作接口占位
        return Task.CompletedTask;
    }
}
