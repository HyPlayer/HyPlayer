using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using System.Threading.Tasks;
using HyPlayer.NeteaseApi;
using HyPlayer.NeteaseApi.Bases;
using HyPlayer.NeteaseApi.Bases.EApiContractBases;

namespace HyPlayer.Features.Radio.Services;

public sealed class PodcastListApi : EApiContractBase<PodcastListRequest, PodcastJsonResponse, ErrorResultBase,
    PodcastListActualRequest>
{
    private PodcastListApi(string apiPath)
    {
        ApiPath = apiPath;
        Url = "https://interface.music.163.com/eapi" + apiPath[4..];
    }

    public static PodcastListApi LikedVoices => new("/api/content/my/liked/voice");
    public static PodcastListApi CollectedVoices => new("/api/content/my/collect/voice");
    public static PodcastListApi CreatedVoiceLists => new("/api/social/my/created/voicelist/v1");

    public override string IdentifyRoute => ApiPath;
    public override string Url { get; protected set; }
    public override string ApiPath { get; protected set; }
    public override HttpMethod Method => HttpMethod.Post;

    public override Task MapRequest(ApiHandlerOption option)
    {
        if (Request is not null)
            ActualRequest = new PodcastListActualRequest
            {
                Limit = Request.Limit,
                Offset = Request.Offset
            };
        return Task.CompletedTask;
    }

    public static void Register(ApiHandlerOption option)
    {
        option.JsonSerializerOptions = new JsonSerializerOptions(option.JsonSerializerOptions)
        {
            TypeInfoResolver = JsonTypeInfoResolver.Combine(
                option.JsonSerializerOptions.TypeInfoResolver,
                PodcastJsonContext.Default)
        };
    }
}

public class PodcastListRequest : RequestBase
{
    public int Limit { get; set; } = 200;
    public int Offset { get; set; }
}

public class PodcastListActualRequest : EApiActualRequestBase
{
    [JsonPropertyName("limit")] public int Limit { get; set; }
    [JsonPropertyName("offset")] public int Offset { get; set; }
}

public class PodcastJsonResponse : CodedResponseBase
{
    [JsonExtensionData] public Dictionary<string, JsonElement>? Payload { get; set; }
}

[JsonSerializable(typeof(PodcastListActualRequest))]
[JsonSerializable(typeof(PodcastJsonResponse))]
public partial class PodcastJsonContext : JsonSerializerContext
{
}
