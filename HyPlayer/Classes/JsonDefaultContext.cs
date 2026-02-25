using HyPlayer.NeteaseApi;
using HyPlayer.NeteaseApi.ApiContracts.Album;
using HyPlayer.NeteaseApi.ApiContracts.Artist;
using HyPlayer.NeteaseApi.ApiContracts.Cloud;
using HyPlayer.NeteaseApi.ApiContracts.Comment;
using HyPlayer.NeteaseApi.ApiContracts.DjChannel;
using HyPlayer.NeteaseApi.ApiContracts.Login;
using HyPlayer.NeteaseApi.ApiContracts.Playlist;
using HyPlayer.NeteaseApi.ApiContracts.Recommend;
using HyPlayer.NeteaseApi.ApiContracts.Song;
using HyPlayer.NeteaseApi.ApiContracts.User;
using HyPlayer.NeteaseApi.Extensions.JsonSerializer;
using HyPlayer.NeteaseApi.Models.ResponseModels;
using LiteFM.Abstractions;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using static HyPlayer.Classes.UpdateManager;

namespace HyPlayer.Classes
{
    [JsonSourceGenerationOptions(WriteIndented = true, Converters = new[] { typeof(JsonBooleanConverter), typeof(JsonObjectStringConverter), typeof(NumberToStringConverter) })]
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
