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
/// <c>lcl</c> — 普通本地音频文件提供者。
/// <para>
/// 从本地路径获取 <see cref="StorageFile"/>，通过
/// <see cref="MediaSource.CreateFromStorageFile"/> 创建媒体源。
/// </para>
/// </summary>
public sealed class LocalFileProvider : IMediaSourceProvider
{
    /// <inheritdoc />
    public string Id => "lcl";

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
