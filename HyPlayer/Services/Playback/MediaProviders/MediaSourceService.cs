#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HyPlayer.Classes;
using HyPlayer.Services.Abstractions;
using Windows.Media.Core;

namespace HyPlayer.Services.Playback.MediaProviders;

/// <summary>
/// 媒体源服务实现，根据 <see cref="HyPlayItem.ProviderId"/> 路由到对应的 <see cref="IMediaSourceProvider"/>。
/// <para>
/// 通过构造函数注入所有已注册的 <see cref="IMediaSourceProvider"/>，
/// 按 <see cref="IMediaSourceProvider.Id"/> 建立字典索引以实现 O(1) 路由。
/// </para>
/// </summary>
public sealed class MediaSourceService : IMediaSourceService
{
    private readonly Dictionary<string, IMediaSourceProvider> _providers;

    /// <summary>
    /// 创建 <see cref="MediaSourceService"/> 实例
    /// </summary>
    /// <param name="providers">所有已注册的媒体源提供者</param>
    public MediaSourceService(IEnumerable<IMediaSourceProvider> providers)
    {
        _providers = providers.ToDictionary(p => p.Id, StringComparer.Ordinal);
    }

    /// <inheritdoc />
    public Task<MediaSource?> CreateMediaSourceAsync(HyPlayItem item, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(item.ProviderId))
        {
            throw new InvalidOperationException(
                $"HyPlayItem '{item.Name}' (Id={item.Id}) 未设置 ProviderId");
        }

        if (!_providers.TryGetValue(item.ProviderId, out var provider))
        {
            throw new NotSupportedException(
                $"未找到 ProviderId='{item.ProviderId}' 对应的 IMediaSourceProvider");
        }

        return provider.CreateAsync(item, ct);
    }
}
