using System.Threading;
using System.Threading.Tasks;
using HyPlayer.Domain.Music;

namespace HyPlayer.Features.Playback.QueueProviders;

/// <summary>
///     队列源提供者 — 每种 <see cref="SongListQueueScopeKind" /> 对应一个实现。
///     负责从远程源加载歌曲列表并返回批次结果。
/// </summary>
public interface IQueueSourceProvider
{
    /// <summary>队列范围类型</summary>
    SongListQueueScopeKind Kind { get; }

    /// <summary>来源 ID 前缀（如 "pl", "al", "rd"），用于兼容 string-based 路由</summary>
    string Prefix { get; }

    /// <summary>该源是否支持完整加载（歌单/专辑/电台）</summary>
    bool SupportCompleteLoad { get; }

    /// <summary>
    ///     根据 ID 加载歌曲批次。
    /// </summary>
    /// <param name="id">源 ID（不含前缀）</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task<ProviderQueueSourceLoadResult> LoadAsync(string id, CancellationToken cancellationToken = default);
}