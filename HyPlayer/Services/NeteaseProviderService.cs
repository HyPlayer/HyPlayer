using HyPlayer.Classes;
using HyPlayer.Contracts.Services;
using HyPlayer.NeteaseApi;
using HyPlayer.NeteaseApi.ApiContracts;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace HyPlayer.Services
{
    public class NeteaseProviderService : INeteaseProviderService
    {
#nullable enable
        private readonly NeteaseCloudMusicApiHandler _apiHandler;
#nullable restore

        public NeteaseProviderService(NeteaseCloudMusicApiHandler apiHandler)
        {
            // Initialize any necessary resources or services here.
            _apiHandler = apiHandler;
        }

        public bool IsLoggedIn => Common.LoginedUser != null;

        public async Task<List<ProvidableItemBase>> GetRecommendedResourceAsync(string typeId, CancellationToken token)
        {
            if (typeId == NeteaseTypeIds.Playlist) // 推荐歌单
            {
                return (await _apiHandler.RequestAsync(NeteaseApis.RecommendPlaylistsApi, token))
                    .Match(success => success.Recommends?.Select(
                        t => (ProvidableItemBase)
                            t.MapToNCPlayList()).ToList(),
                            error => throw new Exception(error.Message));
            }
            if (typeId == NeteaseTypeIds.SingleSong) // 推荐单曲
            {
                return (await _apiHandler.RequestAsync(NeteaseApis.RecommendSongsApi, token))
                    .Match(success => success.Data.DailySongs?.Select(
                        t => (ProvidableItemBase)
                            t.MapToNcSong()).ToList(),
                            error => throw new Exception(error.Message));
            }
            if (typeId == NeteaseTypeIds.Chart) // 榜单
            {
                return (await _apiHandler.RequestAsync(NeteaseApis.ToplistApi, token))
                    .Match(success => success.List?.Select(
                        t => (ProvidableItemBase)
                            t.MapToNCPlayList()).ToList(),
                            error => throw new Exception(error.Message));
            }
            if (typeId == NeteaseTypeIds.PlaylistCategory) // 官方歌单分类
            {
                return (await _apiHandler.RequestAsync(NeteaseApis.PlaylistCategoryListApi, token))
                    .Match(success => success.Playlists?.Select(
                        t => (ProvidableItemBase)
                            t.MapToNCPlayList()).ToList(),
                            error => throw new Exception(error.Message));
            }
            throw new ArgumentException(typeId);
        }
    }
}
