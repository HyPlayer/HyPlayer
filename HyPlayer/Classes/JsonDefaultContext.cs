using HyPlayer.Domain.Lyrics;
using HyPlayer.Infrastructure.Netease;
using HyPlayer.NeteaseApi;
using HyPlayer.Services.History;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using static HyPlayer.Services.Updates.UpdateManager;

namespace HyPlayer.Classes
{
    [JsonSourceGenerationOptions(WriteIndented = true)]
    [JsonSerializable(typeof(The163KeyClass))]
    [JsonSerializable(typeof(AdditionalParameters))]
    [JsonSerializable(typeof(List<string>))]
    [JsonSerializable(typeof(CurPlayingListHistoryState))]
    [JsonSerializable(typeof(HyLyricInfo))]
    [JsonSerializable(typeof(HyALRCLyricInfo))]
    [JsonSerializable(typeof(LatestApplicationUpdate))]
    [JsonSerializable(typeof(GitHubReleaseResponse))]
    public partial class JsonDefaultContext : JsonSerializerContext
    {
    }
}
