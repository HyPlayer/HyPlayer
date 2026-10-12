using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using HyPlayer.NeteaseProvider.Models;
using HyPlayer.PlayCore.Abstraction.Interfaces.PlayListContainer;
using HyPlayer.PlayCore.Abstraction.Interfaces.Provider;
using HyPlayer.PlayCore.Abstraction.Models;
using HyPlayer.PlayCore.Abstraction.Models.Containers;
using HyPlayer.UI.Lists;

namespace HyPlayer.Features.Radio.Services;

internal sealed class PodcastLibraryService : IPodcastLibraryService
{
    private const int PageSize = 200;
    private const int MaxEpisodeCount = 1000;
    private const string EpisodesContainerTypeId = "podcast-episodes";

    private readonly IProviderKnownTypeIds _knownTypeIds;
    private readonly NeteaseProvider.NeteaseProvider _provider;
    private readonly IUserLibraryProvidable _userLibraryProvider;

    public PodcastLibraryService(
        NeteaseProvider.NeteaseProvider provider,
        IUserLibraryProvidable userLibraryProvider,
        IProviderKnownTypeIds knownTypeIds)
    {
        _provider = provider;
        _userLibraryProvider = userLibraryProvider;
        _knownTypeIds = knownTypeIds;
    }

    public ContainerBase CreateEpisodesContainer(string kind)
    {
        var isCollected = kind == PodcastEpisodeKinds.Collected;
        return new DelegateProgressiveContainer(
            (offset, count, cancellationToken) => GetEpisodesPageAsync(isCollected, offset, count, cancellationToken),
            isCollected ? "收藏的单集" : "点赞的单集",
            isCollected ? PodcastEpisodeKinds.Collected : PodcastEpisodeKinds.Liked,
            EpisodesContainerTypeId,
            pageSize: PageSize);
    }

    public async Task<IReadOnlyList<ContainerBase>> GetCreatedChannelsAsync(
        CancellationToken cancellationToken = default)
    {
        var response = await _provider.RequestAsync(
            PodcastListApi.CreatedVoiceLists,
            new PodcastListRequest { Limit = PageSize },
            cancellationToken);
        if (response.IsError) throw (Exception?)response.Error ?? new InvalidOperationException("加载失败");
        if (!TryGetData(response.Value, out var data)) return [];

        var channels = new List<ContainerBase>();
        foreach (var item in Items(data))
        {
            if (item.ValueKind != JsonValueKind.Object) continue;

            var id = Text(item, "voiceListId") ?? Text(item, "id");
            var name = Text(item, "voiceListName") ?? Text(item, "name");
            if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(name)) continue;

            channels.Add(new NeteaseRadioChannel
            {
                ActualId = id,
                Name = name,
                CoverUrl = Text(item, "coverUrl") ?? Text(item, "picUrl")
            });
        }

        return channels;
    }

    public async Task<IReadOnlyList<ContainerBase>> GetSubscribedChannelsAsync(
        CancellationToken cancellationToken = default)
    {
        if (_knownTypeIds.RadioChannelTypeId is not { } radioChannelTypeId) return [];
        if (await _userLibraryProvider.GetCurrentUserLibraryContainerAsync(radioChannelTypeId, cancellationToken)
            is not IProgressiveLoadingContainer container)
            return [];

        var (_, items) = await container.GetProgressiveItemsListAsync(0, PageSize, cancellationToken);
        return items.OfType<ContainerBase>()
            .Where(channel => !string.IsNullOrEmpty(channel.ActualId) && !string.IsNullOrEmpty(channel.Name))
            .ToList();
    }

    private async Task<(bool HasMore, List<ProvidableItemBase> Items)> GetEpisodesPageAsync(
        bool isCollected,
        int offset,
        int count,
        CancellationToken cancellationToken)
    {
        if (offset >= MaxEpisodeCount) return (false, []);

        var response = await _provider.RequestAsync(
            isCollected ? PodcastListApi.CollectedVoices : PodcastListApi.LikedVoices,
            new PodcastListRequest { Limit = count, Offset = offset },
            cancellationToken);
        if (response.IsError) throw (Exception?)response.Error ?? new InvalidOperationException("加载失败");
        if (!TryGetData(response.Value, out var data)) return (false, []);

        var episodes = Items(data).Select(MapEpisode).OfType<ProvidableItemBase>().ToList();
        return (episodes.Count > 0 && Bool(data, "hasMore"), episodes);
    }

    private static bool TryGetData(PodcastJsonResponse? response, out JsonElement data)
    {
        data = default;
        if (response?.Payload is null || !response.Payload.TryGetValue("data", out data)) return false;
        return data.ValueKind is JsonValueKind.Object or JsonValueKind.Array;
    }

    private static IEnumerable<JsonElement> Items(JsonElement data)
    {
        if (data.ValueKind == JsonValueKind.Array) return data.EnumerateArray();
        foreach (var key in (string[])["data", "list"])
            if (data.TryGetProperty(key, out var list) && list.ValueKind == JsonValueKind.Array)
                return list.EnumerateArray();
        return [];
    }

    private static NeteaseRadioProgram? MapEpisode(JsonElement voice)
    {
        if (voice.ValueKind != JsonValueKind.Object) return null;

        var songId = Text(voice, "trackId");
        string? songName = null;
        if (voice.TryGetProperty("mainSong", out var mainSong) && mainSong.ValueKind == JsonValueKind.Object)
        {
            songId ??= Text(mainSong, "id");
            songName = Text(mainSong, "name");
        }

        if (string.IsNullOrEmpty(songId) || songId == "0") return null;

        var programId = Text(voice, "voiceId") ?? Text(voice, "id");
        if (string.IsNullOrEmpty(programId)) return null;

        NeteaseRadioChannel? channel = null;
        string? channelCover = null;
        if (voice.TryGetProperty("radio", out var radio) && radio.ValueKind == JsonValueKind.Object)
        {
            channelCover = Text(radio, "picUrl");
            if (Text(radio, "id") is { Length: > 0 } channelId)
                channel = new NeteaseRadioChannel
                {
                    ActualId = channelId,
                    Name = Text(radio, "name") ?? string.Empty,
                    CoverUrl = channelCover
                };
        }

        var hostName = Text(voice, "userName") ?? string.Empty;
        var host = new NeteaseUser
        {
            ActualId = Text(voice, "userId"),
            Name = hostName
        };
        if (voice.TryGetProperty("dj", out var dj) && dj.ValueKind == JsonValueKind.Object)
            host.AvatarUrl = Text(dj, "avatarUrl");

        var name = Text(voice, "voiceName") ?? songName ?? string.Empty;
        var cover = Text(voice, "coverUrl") ?? channelCover;
        var duration = (long)Number(voice, "duration");
        return new NeteaseRadioProgram
        {
            ActualId = programId,
            Name = name,
            CoverUrl = cover,
            Description = Text(voice, "voiceDesc"),
            ProgramDuration = duration,
            RadioChannel = channel,
            MainSong = new NeteaseSong
            {
                ActualId = songId,
                Name = songName ?? name,
                CoverUrl = cover,
                Artists = [host],
                Duration = duration,
                Available = true
            },
            Host = host,
            CreatorList = string.IsNullOrEmpty(hostName) ? [] : [hostName],
            Duration = duration,
            Available = true
        };
    }

    private static string? Text(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value)) return null;
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            _ => null
        };
    }

    private static double Number(JsonElement element, string name)
    {
        return element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetDouble()
            : 0;
    }

    private static bool Bool(JsonElement element, string name)
    {
        return element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) &&
               value.ValueKind == JsonValueKind.True;
    }
}
