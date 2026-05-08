#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using HyPlayer.Classes;
using HyPlayer.Services.Abstractions;
using Windows.Media.Core;
using Windows.Storage;

namespace HyPlayer.Services.Playback.MediaProviders;

/// <summary>
/// <c>nlo</c> — 网易云歌曲已下载到本地（非 NCM 格式）提供者。
/// <para>
/// 用于 <see cref="HyPlayItem.IsLocalFile"/> 为 <c>true</c> 且文件不是 NCM 格式的网易云歌曲。
/// 直接通过 <see cref="MediaSource.CreateFromStorageFile"/> 创建媒体源。
/// </para>
/// </summary>
public sealed class NeteaseLocalFileProvider : IMediaSourceProvider
{
    /// <inheritdoc />
    public string Id => "nlo";

    /// <inheritdoc />
    public async Task<MediaSource?> CreateAsync(HyPlayItem item, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var file = item.LocalStorageFile;
        if (file == null && !string.IsNullOrEmpty(item.Url))
        {
            file = await StorageFile.GetFileFromPathAsync(item.Url);
            item.LocalStorageFile = file;
        }

        if (file == null)
            return null;

        return MediaSource.CreateFromStorageFile(file);
    }
}
