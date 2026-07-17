using CommunityToolkit.Mvvm.DependencyInjection;
using HyPlayer.Domain.Music;
using HyPlayer.Domain.Settings;
using HyPlayer.PlayCore.Abstraction.Interfaces.ProvidableItem;
using HyPlayer.PlayCore.Abstraction.Interfaces.Provider;
using HyPlayer.PlayCore.Abstraction.Models;
using HyPlayer.PlayCore.Abstraction.Models.Containers;
using HyPlayer.PlayCore.Abstraction.Models.Resources;
using HyPlayer.PlayCore.Abstraction.Models.SingleItems;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace HyPlayer.UI.Lists;

public sealed class ProvidableItemDisplayResolver
{
    private readonly IProviderKnownTypeIds _knownTypeIds;
    private readonly Setting _setting;

    public ProvidableItemDisplayResolver(IProviderKnownTypeIds knownTypeIds, Setting setting)
    {
        _knownTypeIds = knownTypeIds;
        _setting = setting;
    }

    public async Task<ProvidableItemRowViewModel> CreateRowAsync(ProvidableItemBase item, int order, CancellationToken cancellationToken = default)
    {
        var creators = item is IHasCreators creatorsProvider
            ? await creatorsProvider.GetCreatorsAsync(cancellationToken) ?? []
            : [];
        var aliases = item is IHasAliases aliasProvider ? aliasProvider.Aliases ?? [] : [];
        var translation = item is IHasTranslation translationProvider ? translationProvider.Translation : null;
        var album = item is SingleSongBase sg ? sg.Album?.Name : null;
        var track = item as IHasTrackMetadata;
        var richMedia = item as IHasRichMediaReference;
        var coverUrl = _setting.noImage ? null : await TryGetCoverUrlAsync(item, cancellationToken);

        return new ProvidableItemRowViewModel
        {
            Item = item,
            Order = order,
            Translation = string.IsNullOrEmpty(translation) ? null : $"({translation})",
            Title = item.Name ?? item.ActualId ?? string.Empty,
            LineOne = string.Join(" / ",creators.Select(t=>t.Name) ?? []),
            LineTwo = album,
            LineThree = string.Join(" / ", aliases),
            CoverUrl = coverUrl,
            RichMediaId = richMedia?.RichMediaId,
            CanOpenComments = item.TypeId == _knownTypeIds.SingleSongTypeId
                              || item.TypeId == _knownTypeIds.PlaylistTypeId
                              || item.TypeId == _knownTypeIds.RichMediaTypeId,
            CanOpenRichMedia = !string.IsNullOrWhiteSpace(richMedia?.RichMediaId),
            CanOpenCreators = creators.Count > 0,
            CanDownload = item.TypeId == _knownTypeIds.SingleSongTypeId,
            CanCollect = item.TypeId == _knownTypeIds.SingleSongTypeId,
            IsAvailable = true,
            Creators = creators,
            Album = item as AlbumBase,
            GroupKey = string.IsNullOrWhiteSpace(track?.DiscName) ? string.Empty : track.DiscName
        };
    }

    public static ProvidableItemDisplayResolver CreateDefault()
    {
        return new ProvidableItemDisplayResolver(
            Ioc.Default.GetRequiredService<IProviderKnownTypeIds>(),
            Ioc.Default.GetRequiredService<Setting>());
    }

    private static async Task<string?> TryGetCoverUrlAsync(object? item, CancellationToken cancellationToken)
    {
        if (item is not IHasCover coverProvider)
            return null;

        var result = await coverProvider.GetCoverAsync(ctk: cancellationToken);
        return result is IResourceResultOf<Uri?> uriResult
            ? (await uriResult.GetResourceAsync(cancellationToken))?.GetLeftPart(UriPartial.Path)
            : null;
    }
}
