using HyPlayer.Domain.Lyrics;
using HyPlayer.Domain;
using HyPlayer.Infrastructure.Diagnostics;
using HyPlayer.Infrastructure.Netease;
using HyPlayer.Services.Abstractions;
using HyPlayer.Services.History;
using LiteFM.Abstractions;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using static HyPlayer.Services.Updates.UpdateManager;

namespace HyPlayer.Classes
{
    [JsonSourceGenerationOptions(WriteIndented = true)]
    [JsonSerializable(typeof(The163KeyClass))]
    [JsonSerializable(typeof(List<string>))]
    [JsonSerializable(typeof(CurPlayingListHistoryState))]
    [JsonSerializable(typeof(HyLyricInfo))]
    [JsonSerializable(typeof(HyALRCLyricInfo))]
    [JsonSerializable(typeof(LastFMSession))]
    [JsonSerializable(typeof(DumpInfo))]
    [JsonSerializable(typeof(PlaybackCurrentItemSnapshot))]
    [JsonSerializable(typeof(CommentUserInfo))]
    [JsonSerializable(typeof(LatestApplicationUpdate))]
    [JsonSerializable(typeof(GitHubReleaseResponse))]
    public partial class JsonDefaultContext : JsonSerializerContext
    {
    }
}
