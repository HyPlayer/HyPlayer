using HyPlayer.Domain.Music;
using HyPlayer.PlayCore.Abstraction;
using HyPlayer.PlayCore.Abstraction.Interfaces.Provider;
using HyPlayer.PlayCore.Abstraction.Models.Resources;
using HyPlayer.PlayCore.Abstraction.Models.SingleItems;
using HyPlayer.Services.Abstractions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace HyPlayer.Services.Playback.PlayCoreBridge;

public sealed class LegacyMediaSourceResourceProvider(IMediaSourceService mediaSourceService) : ProviderBase, IMusicResourceProvidable
{
    public override string Name => "HyPlayer legacy media source bridge";

    public override string Id => "lcl";

    public override List<ProvidableTypeId> ProvidableTypeIds =>
        [
            new("sg", "本地歌曲", true),
            new("ncm", "NCM 歌曲", true),
        ];

    public async Task<MusicResourceBase?> GetMusicResourceAsync(
        SingleSongBase song,
        ResourceQualityTag? qualityTag = null,
        CancellationToken ctk = default)
    {
        ctk.ThrowIfCancellationRequested();

        if (song.ProviderId != Id || song.TypeId is not ("sg" or "ncm") || string.IsNullOrWhiteSpace(song.ActualId))
        {
            return null;
        }

        var item = song.ToHyPlayItem();
        item.Url = song.ActualId;
        item.Id = string.IsNullOrWhiteSpace(item.Id) ? song.ActualId : item.Id;

        if (song.TypeId == "ncm")
        {
            item.ItemType = HyPlayItemType.Local;
            item.SubExt = ".ncm";
        }
        else
        {
            item.ItemType = HyPlayItemType.Local;
            var extensionName = Path.GetExtension(song.ActualId) ?? string.Empty;
            item.SubExt = string.Equals(extensionName, ".ncm", StringComparison.OrdinalIgnoreCase)
                ? string.Empty
                : extensionName;
        }

        var mediaSource = await mediaSourceService.CreateMediaSourceAsync(item, ctk);
        return new LegacyMediaSourceMusicResource
        {
            LegacyItem = item,
            LegacyMediaSource = mediaSource,
            SuggestedVolume = item.Volume,
            ResourceName = item.Name,
            HasContent = mediaSource is not null,
            ExtensionName = item.SubExt
        };
    }
}
