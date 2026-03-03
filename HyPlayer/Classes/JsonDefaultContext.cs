using HyPlayer.NeteaseApi;
using HyPlayer.NeteaseApi.Extensions.JsonSerializer;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using static HyPlayer.Classes.UpdateManager;

namespace HyPlayer.Classes
{
    [JsonSourceGenerationOptions(WriteIndented = true)]
    [JsonSerializable(typeof(The163KeyClass))]
    [JsonSerializable(typeof(AdditionalParameters))]
    [JsonSerializable(typeof(List<string>))]
    [JsonSerializable(typeof(HyLyricInfo))]
    [JsonSerializable(typeof(HyALRCLyricInfo))]
    [JsonSerializable(typeof(LatestApplicationUpdate))]
    [JsonSerializable(typeof(GitHubReleaseResponse))]
    public partial class JsonDefaultContext : JsonSerializerContext
    {
    }
}
