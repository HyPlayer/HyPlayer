#nullable enable
using System;
using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
using System.Threading.Tasks;
using HyPlayer.Classes;
using HyPlayer.Services.Abstractions;
using Windows.Media.Core;
using Windows.Storage;
using Windows.Storage.Streams;

namespace HyPlayer.Services.Playback.MediaProviders;

/// <summary>
/// <c>ncm</c> — NCM 加密文件提供者。
/// <para>
/// 使用 <see cref="NCMFile"/> 解密 NCM 文件，将解密后的音频流通过
/// <see cref="MediaSource.CreateFromStream"/> 创建媒体源。
/// </para>
/// </summary>
public sealed class NcmFileProvider : IMediaSourceProvider
{
    /// <inheritdoc />
    public string Id => "ncm";

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

        // 使用 Polly 重试策略解析 NCM 文件
        await RetryPolicies.NcmFileLoadPolicy.ExecuteAsync(async () =>
        {
            ct.ThrowIfCancellationRequested();

            using var stream = await file.OpenStreamForReadAsync();
            if (!NCMFile.IsCorrectNCMFile(stream))
            {
                throw new InvalidOperationException("NCM 文件格式不正确");
            }

            item.PlayItem ??= new PlayItem();
            var info = NCMFile.GetNCMMusicInfo(stream);
            item.PlayItem.CoverBuffer = NCMFile.GetCoverByteArray(stream).AsBuffer();
            using var encStream = NCMFile.GetEncryptedStream(stream);
            encStream.Seek(0, SeekOrigin.Begin);

            var songDataStream = new InMemoryRandomAccessStream();
            var targetStream = songDataStream.AsStream();
            encStream.CopyTo(targetStream);
            item.PlayItem.NcmPlayableStream = songDataStream;
            item.PlayItem.NcmPlayableStreamMIMEType = MIMEHelper.GetNCMFileMimeType(info.format);


        });

        if (item.PlayItem?.NcmPlayableStream == null)
            return null;

        return MediaSource.CreateFromStream(
            item.PlayItem.NcmPlayableStream,
            item.PlayItem.NcmPlayableStreamMIMEType);
    }
}
