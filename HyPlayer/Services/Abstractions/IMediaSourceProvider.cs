using System.Threading;
using System.Threading.Tasks;
using HyPlayer.Classes;
using Windows.Media.Core;

namespace HyPlayer.Services.Abstractions;

/// <summary>
/// 媒体源提供者，负责为指定类型的 <see cref="HyPlayItem"/> 创建 <see cref="MediaSource"/>。
/// <para>
/// 每种提供者用三字母 Id 标识，在 <see cref="HyPlayItem.ProviderId"/> 中指定：
/// <list type="bullet">
///   <item><c>lcl</c> — 普通本地音频文件</item>
///   <item><c>ncm</c> — NCM 加密文件（解密后播放）</item>
///   <item><c>nlo</c> — 网易云歌曲已下载到本地（非 NCM 格式）</item>
///   <item><c>nca</c> — 网易云在线播放 + 缓存策略（边下边播）</item>
///   <item><c>nst</c> — 网易云纯流式播放（不缓存）</item>
/// </list>
/// </para>
/// </summary>
public interface IMediaSourceProvider
{
    /// <summary>三字母提供者标识</summary>
    string Id { get; }

    /// <summary>
    /// 为指定曲目创建媒体源
    /// </summary>
    Task<MediaSource?> CreateAsync(HyPlayItem item, CancellationToken ct);
}

/// <summary>
/// 媒体源服务，根据 <see cref="HyPlayItem.ProviderId"/> 路由到对应的 <see cref="IMediaSourceProvider"/>
/// </summary>
public interface IMediaSourceService
{
    /// <summary>
    /// 为指定曲目创建媒体源
    /// </summary>
    Task<MediaSource?> CreateMediaSourceAsync(HyPlayItem item, CancellationToken ct);
}
