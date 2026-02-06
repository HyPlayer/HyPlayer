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
using HyPlayer.NeteaseApi.Models.ResponseModels;
using LiteFM.Abstractions;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using static HyPlayer.Classes.UpdateManager;

namespace HyPlayer.Classes
{
    [JsonSourceGenerationOptions(WriteIndented = true)]
    [JsonSerializable(typeof(The163KeyClass))]
    [JsonSerializable(typeof(AdditionalParameters))]
    [JsonSerializable(typeof(List<string>))]
    [JsonSerializable(typeof(CommentFloorResponse))]
    [JsonSerializable(typeof(SongUrlResponse))]
    [JsonSerializable(typeof(HyLyricInfo))]
    [JsonSerializable(typeof(HyALRCLyricInfo))]
    [JsonSerializable(typeof(LyricResponse))]
    [JsonSerializable(typeof(EmittedSongDto))]
    [JsonSerializable(typeof(EmittedSongDtoWithPrivilege[]))]
    [JsonSerializable(typeof(AlbumResponse))]
    [JsonSerializable(typeof(PlaylistTracksGetResponse))]
    [JsonSerializable(typeof(SongDetailResponse))]
    [JsonSerializable(typeof(ArtistDetailResponse))]
    [JsonSerializable(typeof(ArtistSongsResponse))]
    [JsonSerializable(typeof(ArtistAlbumsResponse))]
    [JsonSerializable(typeof(LoginStatusResponse))]
    [JsonSerializable(typeof(LikelistResponse))]
    [JsonSerializable(typeof(UserPlaylistResponse))]
    [JsonSerializable(typeof(UserDetailResponse))]
    [JsonSerializable(typeof(UserCloudResponse))]
    [JsonSerializable(typeof(DjChannelSubscribedResponse))]
    [JsonSerializable(typeof(ArtistSublistResponse))]
    [JsonSerializable(typeof(AlbumSublistResponse))]
    [JsonSerializable(typeof(DjChannelProgramsResponse))]
    [JsonSerializable(typeof(DjChannelDetailResponse))]
    [JsonSerializable(typeof(RecommendSongsResponse))]
    [JsonSerializable(typeof(PlaylistTracksGetResponse))]
    [JsonSerializable(typeof(PlaylistDetailResponse))]
    [JsonSerializable(typeof(LatestApplicationUpdate))]
    [JsonSerializable(typeof(LastFMSession))]
    [JsonSerializable(typeof(AlbumDetailDynamicResponse))]
    public partial class JsonDefaultContext : JsonSerializerContext
    {
    }
}
