# Task 1 - Current-Operation Migration Inventory

Generated from repository grep/read inspection for direct NetEase Cloud Music API usage under `HyPlayer/` (production source only; `bin/` and `obj/` generated files excluded).

## Summary

- Total direct NetEase API files: 48
- Total grep matches (`NeteaseApi|NeteaseApis|NeteaseCloudMusicApiHandler`): 300
- Total direct endpoint operations (`NeteaseApis.*`): 96
- Files with direct endpoint operations: 36
- Unique endpoint APIs: 64
- Operations marked `NEEDS NEW CONTRACT`: 57
- Migration risk distribution: H=28, M=55, L=13
- Playback-management dependency files: 67

## Auth/Session Ownership Decision

NetEase session state should move into `NeteaseProvider` / provider context. App-side services should stop owning `NeteaseCloudMusicApiHandler.Option.Cookies`, `FakeCheckToken`, login status probing, and QR/device announce flows. HyPlayer should retain only provider-neutral app preferences and call a provider auth/session contract once that contract exists.

## Existing Target Abstractions Read

| Abstraction | Existing capability | Notes |
|---|---|---|
| `IMusicResourceProvidable.GetMusicResourceAsync` | Song audio URL/resource lookup | Existing `NeteaseProvider` maps `SongUrlApi`. Video/mlog URLs still need a contract. |
| `ILyricProvidable.GetLyricInfoAsync` | Raw lyric lookup | Existing `NeteaseProvider` maps `LyricApi`. |
| `IProvableItemLikable` | Like/unlike and liked ids | Existing `NeteaseProvider` uses null target for heart-like and non-null target for playlist add/remove/subscribe-style operations. |
| `IProvidableItemProvidable` | Single item lookup by provider id | Existing target for song/album/artist/playlist/radio/user item details where a single item is requested. |
| `IProvidableItemRangeProvidable` | Multiple item lookup by ids | Existing target for batched song details and playlist/artist content ranges, but paged/container-specific methods may need richer contracts. |
| `ISearchableProvider.SearchProvidableItemsAsync` | Search by keyword/type | Existing target for `SearchApi`; suggestions are not covered. |
| `IRecommendationProvidable.GetRecommendationAsync` | Provider recommendations by optional type id | Existing target for playlists, daily songs, toplists; stateful Personal FM and video/mlog feeds need clearer contracts. |
| `PlayListManagerBase`, `PlayControllerBase`, `AudioServiceBase` | PlayCore playback/control abstractions | Candidate migration targets for playback management, but current UWP `AudioGraphPlayer`/`MediaSource` implementation remains app-side unless PlayCore gains UWP audio surface. |

## Direct NetEase Operation Inventory

| File | Line | Line context | Operation | Category | Target abstraction | Risk |
|---|---:|---|---|---|---|:---:|
| `HyPlayer\Features\Album\AlbumPageViewModel.cs` | 75 | `var json = await _api.RequestAsync(NeteaseApis.AlbumDetailDynamicApi,` | `AlbumDetailDynamicApi` | Song/Album/Artist/Playlist details | IProvidableItemProvidable plus NEEDS NEW CONTRACT for dynamic metadata | M |
| `HyPlayer\Features\Album\AlbumPageViewModel.cs` | 94 | `var json = await _api.RequestAsync(NeteaseApis.AlbumApi,` | `AlbumApi` | Song/Album/Artist/Playlist details | IProvidableItemProvidable.GetProvidableItemByIdAsync | M |
| `HyPlayer\Features\Album\AlbumPageViewModel.cs` | 187 | `_taskRunner.Forget(_api.RequestAsync(NeteaseApis.AlbumSubscribeApi,` | `AlbumSubscribeApi` | Like/Unlike | IProvableItemLikable for album subscribe/unsubscribe | L |
| `HyPlayer\Features\Artist\ArtistPageViewModel.cs` | 64 | `var resp = await _api.RequestAsync(NeteaseApis.ArtistDetailApi,` | `ArtistDetailApi` | Song/Album/Artist/Playlist details | IProvidableItemProvidable.GetProvidableItemByIdAsync | M |
| `HyPlayer\Features\Artist\ArtistPageViewModel.cs` | 106 | `var j1res = await _api.RequestAsync(NeteaseApis.ArtistTopSongApi,` | `ArtistTopSongApi` | Song/Album/Artist/Playlist details | IProvidableItemProvidable.GetProvidableItemByIdAsync or IProvidableItemRangeProvidable for artist songs | M |
| `HyPlayer\Features\Artist\ArtistPageViewModel.cs` | 119 | `var json = await _api.RequestAsync(NeteaseApis.SongDetailApi,` | `SongDetailApi` | Song/Album/Artist/Playlist details | IProvidableItemProvidable.GetProvidableItemByIdAsync / IProvidableItemRangeProvidable.GetProvidableItemsRangeAsync | M |
| `HyPlayer\Features\Artist\ArtistPageViewModel.cs` | 147 | `var resp = await _api.RequestAsync(NeteaseApis.ArtistSongsApi,` | `ArtistSongsApi` | Song/Album/Artist/Playlist details | IProvidableItemRangeProvidable or NEEDS NEW CONTRACT for paged artist songs | M |
| `HyPlayer\Features\Artist\ArtistPageViewModel.cs` | 175 | `var resp = await _api.RequestAsync(NeteaseApis.ArtistAlbumsApi,` | `ArtistAlbumsApi` | Song/Album/Artist/Playlist details | IProvidableItemRangeProvidable or NEEDS NEW CONTRACT for artist albums | M |
| `HyPlayer\Features\Comments\Comments.xaml.cs` | 117 | `var result = await _api.RequestAsync(NeteaseApis.CommentsApi, new CommentsRequest` | `CommentsApi` | Comment operations | NEEDS NEW CONTRACT: comments query provider | M |
| `HyPlayer\Features\Home\HomePage.xaml.cs` | 57 | `var result = await _api.RequestAsync(NeteaseApis.PlaylistPrivacyApi,` | `PlaylistPrivacyApi` | Create/Delete/Privacy playlist | NEEDS NEW CONTRACT: playlist privacy management | H |
| `HyPlayer\Features\Home\HomePage.xaml.cs` | 76 | `var result = await _api.RequestAsync(NeteaseApis.PlaylistDeleteApi,` | `PlaylistDeleteApi` | Create/Delete/Privacy playlist | NEEDS NEW CONTRACT: playlist management delete | H |
| `HyPlayer\Features\Home\HomeViewModel.cs` | 41 | `var rcmdListResult = await _neteaseApi.RequestAsync(NeteaseApis.RecommendPlaylistsApi);` | `RecommendPlaylistsApi` | Recommendations | IRecommendationProvidable.GetRecommendationAsync(typeId: playlist) | L |
| `HyPlayer\Features\Home\HomeViewModel.cs` | 42 | `var topListResult = await _neteaseApi.RequestAsync(NeteaseApis.ToplistApi);` | `ToplistApi` | Recommendations | IRecommendationProvidable.GetRecommendationAsync(typeId: toplist) | L |
| `HyPlayer\Features\Home\HomeViewModel.cs` | 43 | `var categoryListResult = await _neteaseApi.RequestAsync(NeteaseApis.PlaylistCategoryListApi);` | `PlaylistCategoryListApi` | Recommendations | NEEDS NEW CONTRACT: playlist category catalog | M |
| `HyPlayer\Features\Home\HomeViewModel.cs` | 44 | `var rcmdSongsResult = await _neteaseApi.RequestAsync(NeteaseApis.RecommendSongsApi);` | `RecommendSongsApi` | Daily Songs | IRecommendationProvidable.GetRecommendationAsync(typeId: daily-songs) | L |
| `HyPlayer\Features\Library\FavoriteViewModel.cs` | 61 | `var json = await _api.RequestAsync(NeteaseApis.DjChannelSubscribedApi);` | `DjChannelSubscribedApi` | User Library | NEEDS NEW CONTRACT or IProvableItemLikable.GetLikedProvidableIdsAsync for radio library | M |
| `HyPlayer\Features\Library\FavoriteViewModel.cs` | 97 | `var json = await _api.RequestAsync(NeteaseApis.ArtistSublistApi,` | `ArtistSublistApi` | User Library | NEEDS NEW CONTRACT or IProvableItemLikable.GetLikedProvidableIdsAsync for artist library | M |
| `HyPlayer\Features\Library\FavoriteViewModel.cs` | 135 | `var jv = await _api!.RequestAsync(NeteaseApis.AlbumSublistApi,` | `AlbumSublistApi` | User Library | NEEDS NEW CONTRACT or IProvableItemLikable.GetLikedProvidableIdsAsync for album library | M |
| `HyPlayer\Features\Library\HIstoryPage.xaml.cs` | 106 | `var response = await _api.RequestAsync<UserRecordAllResponse, UserRecordRequest, UserRecordResponse, ErrorResultBase, UserRecordActualRequest>(NeteaseApis.UserRecordApi,` | `UserRecordApi` | User Library | NEEDS NEW CONTRACT: user listening history | M |
| `HyPlayer\Features\Library\HIstoryPage.xaml.cs` | 116 | `var response = await _api.RequestAsync<UserRecordWeekResponse, UserRecordRequest, UserRecordResponse, ErrorResultBase, UserRecordActualRequest>(NeteaseApis.UserRecordApi,` | `UserRecordApi` | User Library | NEEDS NEW CONTRACT: user listening history | M |
| `HyPlayer\Features\Library\MusicCloudPage.xaml.cs` | 63 | `var json = await _api.RequestAsync(NeteaseApis.CloudGetApi,` | `CloudGetApi` | User Library | NEEDS NEW CONTRACT: user library/cloud operation | M |
| `HyPlayer\Features\Playlist\SongListViewModel.cs` | 109 | `var json = await _api.RequestAsync(NeteaseApis.PlaylistDetailApi,` | `PlaylistDetailApi` | Song/Album/Artist/Playlist details | IProvidableItemProvidable.GetProvidableItemByIdAsync | M |
| `HyPlayer\Features\Playlist\SongListViewModel.cs` | 177 | `var json = await _api.RequestAsync(NeteaseApis.RecommendSongsApi);` | `RecommendSongsApi` | Daily Songs | IRecommendationProvidable.GetRecommendationAsync(typeId: daily-songs) | L |
| `HyPlayer\Features\Playlist\SongListViewModel.cs` | 208 | `var rst = await _api.RequestAsync(NeteaseApis.PlaylistTracksGetApi,` | `PlaylistTracksGetApi` | Song/Album/Artist/Playlist details | IProvidableItemProvidable/GetProvidableItemsRangeAsync for playlist contents | M |
| `HyPlayer\Features\Playlist\SongListViewModel.cs` | 243 | `var json = await _api.RequestAsync(NeteaseApis.SongDetailApi,` | `SongDetailApi` | Song/Album/Artist/Playlist details | IProvidableItemProvidable.GetProvidableItemByIdAsync / IProvidableItemRangeProvidable.GetProvidableItemsRangeAsync | M |
| `HyPlayer\Features\Playlist\SongListViewModel.cs` | 386 | `var result = await _api.RequestAsync(NeteaseApis.PlaylistSubscribeApi,` | `PlaylistSubscribeApi` | Like/Unlike | IProvableItemLikable for playlist subscribe/unsubscribe | L |
| `HyPlayer\Features\Radio\RadioPage.xaml.cs` | 90 | `var rest = await _api.RequestAsync(NeteaseApis.DjChannelProgramsApi,` | `DjChannelProgramsApi` | Song/Album/Artist/Playlist details | IProvidableItemRangeProvidable or NEEDS NEW CONTRACT for radio programs | M |
| `HyPlayer\Features\Radio\RadioPage.xaml.cs` | 133 | `var json = await _api.RequestAsync(NeteaseApis.DjChannelDetailApi,` | `DjChannelDetailApi` | Song/Album/Artist/Playlist details | IProvidableItemProvidable.GetProvidableItemByIdAsync for radio channel | M |
| `HyPlayer\Features\Radio\RadioPage.xaml.cs` | 253 | `var rest = await _api.RequestAsync(NeteaseApis.DjChannelProgramsApi,` | `DjChannelProgramsApi` | Song/Album/Artist/Playlist details | IProvidableItemRangeProvidable or NEEDS NEW CONTRACT for radio programs | M |
| `HyPlayer\Features\Search\Search.xaml.cs` | 161 | `SearchRequest, SearchResponse, ErrorResultBase, SearchActualRequest>(NeteaseApis.SearchApi,` | `SearchApi` | Search | ISearchableProvider.SearchProvidableItemsAsync | M |
| `HyPlayer\Features\Search\Search.xaml.cs` | 213 | `SearchRequest, SearchResponse, ErrorResultBase, SearchActualRequest>(NeteaseApis.SearchApi,` | `SearchApi` | Search | ISearchableProvider.SearchProvidableItemsAsync | M |
| `HyPlayer\Features\Search\Search.xaml.cs` | 266 | `SearchRequest, SearchResponse, ErrorResultBase, SearchActualRequest>(NeteaseApis.SearchApi,` | `SearchApi` | Search | ISearchableProvider.SearchProvidableItemsAsync | M |
| `HyPlayer\Features\Search\Search.xaml.cs` | 319 | `SearchRequest, SearchResponse, ErrorResultBase, SearchActualRequest>(NeteaseApis.SearchApi,` | `SearchApi` | Search | ISearchableProvider.SearchProvidableItemsAsync | M |
| `HyPlayer\Features\Search\Search.xaml.cs` | 372 | `SearchRequest, SearchResponse, ErrorResultBase, SearchActualRequest>(NeteaseApis.SearchApi,` | `SearchApi` | Search | ISearchableProvider.SearchProvidableItemsAsync | M |
| `HyPlayer\Features\Search\Search.xaml.cs` | 422 | `SearchRequest, SearchResponse, ErrorResultBase, SearchActualRequest>(NeteaseApis.SearchApi,` | `SearchApi` | Search | ISearchableProvider.SearchProvidableItemsAsync | M |
| `HyPlayer\Features\Search\Search.xaml.cs` | 477 | `SearchRequest, SearchResponse, ErrorResultBase, SearchActualRequest>(NeteaseApis.SearchApi,` | `SearchApi` | Search | ISearchableProvider.SearchProvidableItemsAsync | M |
| `HyPlayer\Features\Search\Search.xaml.cs` | 526 | `SearchRequest, SearchResponse, ErrorResultBase, SearchActualRequest>(NeteaseApis.SearchApi,` | `SearchApi` | Search | ISearchableProvider.SearchProvidableItemsAsync | M |
| `HyPlayer\Features\Search\Search.xaml.cs` | 577 | `SearchRequest, SearchResponse, ErrorResultBase, SearchActualRequest>(NeteaseApis.SearchApi,` | `SearchApi` | Search | ISearchableProvider.SearchProvidableItemsAsync | M |
| `HyPlayer\Features\Search\Search.xaml.cs` | 674 | `var json = await _api.RequestAsync(NeteaseApis.SearchSuggestionApi,` | `SearchSuggestionApi` | Search | NEEDS NEW CONTRACT: search suggestion provider | M |
| `HyPlayer\Features\User\MeViewModel.cs` | 40 | `var json = await _neteaseApi.RequestAsync(NeteaseApis.UserDetailApi,` | `UserDetailApi` | User Library | NEEDS NEW CONTRACT: user library/cloud operation | M |
| `HyPlayer\Features\User\MeViewModel.cs` | 63 | `var json = await _neteaseApi.RequestAsync(NeteaseApis.UserPlaylistApi,` | `UserPlaylistApi` | User Library | NEEDS NEW CONTRACT: user library playlist enumeration | H |
| `HyPlayer\Features\Video\MVPage.xaml.cs` | 77 | `var json = await _api.RequestAsync(NeteaseApis.MlogRcmdFeedListApi,` | `MlogRcmdFeedListApi` | Recommendations | NEEDS NEW CONTRACT: video/mlog feed recommendation | M |
| `HyPlayer\Features\Video\MVPage.xaml.cs` | 159 | `var json = await _api.RequestAsync(NeteaseApis.VideoUrlApi,` | `VideoUrlApi` | Stream URL | NEEDS NEW CONTRACT: video media resource provider | M |
| `HyPlayer\Features\Video\MVPage.xaml.cs` | 175 | `var json = await _api.RequestAsync(NeteaseApis.MlogUrlApi,` | `MlogUrlApi` | Stream URL | NEEDS NEW CONTRACT: mlog media resource provider | M |
| `HyPlayer\Features\Video\MVPage.xaml.cs` | 201 | `var json = await _api.RequestAsync(NeteaseApis.VideoDetailApi,` | `VideoDetailApi` | Song/Album/Artist/Playlist details | NEEDS NEW CONTRACT: video detail provider | M |
| `HyPlayer\Features\Video\MVPage.xaml.cs` | 226 | `var json = await _api.RequestAsync(NeteaseApis.MlogDetailApi,` | `MlogDetailApi` | Song/Album/Artist/Playlist details | NEEDS NEW CONTRACT: mlog detail provider | M |
| `HyPlayer\Infrastructure\Netease\Api.cs` | 26 | `var requestResult = await api.RequestAsync(NeteaseApis.LikeApi,` | `LikeApi` | Like/Unlike | IProvableItemLikable.LikeProvidableItemAsync / UnlikeProvidableItemAsync (null target) | L |
| `HyPlayer\Infrastructure\Netease\Api.cs` | 49 | `var jsoon = await api.RequestAsync(NeteaseApis.PlaymodeIntelligenceListApi,` | `PlaymodeIntelligenceListApi` | Recommendations | NEEDS NEW CONTRACT: intelligence playmode recommendation | M |
| `HyPlayer\Infrastructure\Netease\CloudUpload.cs` | 68 | `var checkResult = await api.RequestAsync(NeteaseApis.CloudUploadCheckApi,` | `CloudUploadCheckApi` | User Library | NEEDS NEW CONTRACT: cloud upload workflow | H |
| `HyPlayer\Infrastructure\Netease\CloudUpload.cs` | 102 | `var tokenRes = await api.RequestAsync(NeteaseApis.CloudUploadTokenAllocApi, tokenRequest);` | `CloudUploadTokenAllocApi` | User Library | NEEDS NEW CONTRACT: cloud upload workflow | H |
| `HyPlayer\Infrastructure\Netease\CloudUpload.cs` | 115 | `var loadBalancerRes = await api.RequestAsync(NeteaseApis.NeteaseUploadLoadBalancerGetApi,` | `NeteaseUploadLoadBalancerGetApi` | User Library | NEEDS NEW CONTRACT: cloud upload host discovery | H |
| `HyPlayer\Infrastructure\Netease\CloudUpload.cs` | 141 | `var coverAllocRes = await api.RequestAsync(NeteaseApis.CloudUploadCoverTokenAllocApi,` | `CloudUploadCoverTokenAllocApi` | User Library | NEEDS NEW CONTRACT: cloud upload cover workflow | H |
| `HyPlayer\Infrastructure\Netease\CloudUpload.cs` | 157 | `var imgloadBalancerRes = await api.RequestAsync(NeteaseApis.NeteaseUploadLoadBalancerGetApi,` | `NeteaseUploadLoadBalancerGetApi` | User Library | NEEDS NEW CONTRACT: cloud upload host discovery | H |
| `HyPlayer\Infrastructure\Netease\CloudUpload.cs` | 171 | `var infoRes = await api.RequestAsync(NeteaseApis.CloudUploadInfoApi, infoReq);` | `CloudUploadInfoApi` | User Library | NEEDS NEW CONTRACT: cloud upload metadata commit | H |
| `HyPlayer\Infrastructure\Netease\CloudUpload.cs` | 181 | `var cloudPubRes = await api.RequestAsync(NeteaseApis.CloudPubApi, cloudPubReq);` | `CloudPubApi` | User Library | NEEDS NEW CONTRACT: cloud upload publish | H |
| `HyPlayer\Infrastructure\Netease\ListenTogetherManager.cs` | 72 | `var res = await mgr._api.RequestAsync(NeteaseApis.ListenTogetherRoomCreateApi,` | `ListenTogetherRoomCreateApi` | Listen Together | NEEDS NEW CONTRACT: listen-together room lifecycle | H |
| `HyPlayer\Infrastructure\Netease\ListenTogetherManager.cs` | 133 | `var res = await api.RequestAsync(NeteaseApis.ListenTogetherRoomCheckApi,` | `ListenTogetherRoomCheckApi` | Listen Together | NEEDS NEW CONTRACT: listen-together join/check | H |
| `HyPlayer\Infrastructure\Netease\ListenTogetherManager.cs` | 198 | `_ = _api.RequestAsync(NeteaseApis.ListenTogetherPlayCommandApi, req);` | `ListenTogetherPlayCommandApi` | Listen Together | NEEDS NEW CONTRACT: listen-together playback command sync | H |
| `HyPlayer\Infrastructure\Netease\ListenTogetherManager.cs` | 221 | `_ = _api.RequestAsync(NeteaseApis.ListenTogetherPlayCommandApi, req);` | `ListenTogetherPlayCommandApi` | Listen Together | NEEDS NEW CONTRACT: listen-together playback command sync | H |
| `HyPlayer\Infrastructure\Netease\ListenTogetherManager.cs` | 244 | `_ = _api.RequestAsync(NeteaseApis.ListenTogetherPlayCommandApi, req);` | `ListenTogetherPlayCommandApi` | Listen Together | NEEDS NEW CONTRACT: listen-together playback command sync | H |
| `HyPlayer\Infrastructure\Netease\ListenTogetherManager.cs` | 257 | `_ = _api.RequestAsync(NeteaseApis.ListenTogetherPlayCommandApi,` | `ListenTogetherPlayCommandApi` | Listen Together | NEEDS NEW CONTRACT: listen-together playback command sync | H |
| `HyPlayer\Infrastructure\Netease\ListenTogetherManager.cs` | 297 | `_ = _api.RequestAsync(NeteaseApis.ListenTogetherSyncListReportApi, req);` | `ListenTogetherSyncListReportApi` | Listen Together | NEEDS NEW CONTRACT: listen-together queue sync | H |
| `HyPlayer\Infrastructure\Netease\ListenTogetherManager.cs` | 314 | `var res = await _api.RequestAsync(NeteaseApis.ListenTogetherStatusApi,` | `ListenTogetherStatusApi` | Listen Together | NEEDS NEW CONTRACT: listen-together heartbeat/status | H |
| `HyPlayer\Services\Authentication\AuthService.cs` | 109 | `var response = await _api.RequestAsync(NeteaseApis.LoginCellphoneApi,` | `LoginCellphoneApi` | Login/Logout/Session | NEEDS NEW CONTRACT: provider-owned NetEase auth/session | H |
| `HyPlayer\Services\Authentication\AuthService.cs` | 121 | `var response = await _api.RequestAsync(NeteaseApis.LoginEmailApi,` | `LoginEmailApi` | Login/Logout/Session | NEEDS NEW CONTRACT: provider-owned NetEase auth/session | H |
| `HyPlayer\Services\Authentication\AuthService.cs` | 138 | `var key = await _api.RequestAsync(NeteaseApis.LoginQrCodeUnikeyApi, new LoginQrCodeUnikeyRequest());` | `LoginQrCodeUnikeyApi` | Login/Logout/Session | NEEDS NEW CONTRACT: provider-owned QR login session | H |
| `HyPlayer\Services\Authentication\AuthService.cs` | 147 | `var res = await _api.RequestAsync(NeteaseApis.LoginQrCodeCheckApi,` | `LoginQrCodeCheckApi` | Login/Logout/Session | NEEDS NEW CONTRACT: provider-owned QR login polling | H |
| `HyPlayer\Services\Authentication\AuthService.cs` | 163 | `var rst = await _api.RequestAsync(NeteaseApis.LoginAnnounceDeviceApi, new LoginAnnounceDeviceRequest` | `LoginAnnounceDeviceApi` | Login/Logout/Session | NEEDS NEW CONTRACT: provider-owned device announce | H |
| `HyPlayer\Services\Authentication\AuthService.cs` | 188 | `var statusResult = await _api.RequestAsync(NeteaseApis.LoginStatusApi);` | `LoginStatusApi` | Login/Logout/Session | NEEDS NEW CONTRACT: provider-owned login status/session restore | H |
| `HyPlayer\Services\Authentication\AuthService.cs` | 309 | `var js = await _api.RequestAsync(NeteaseApis.LikelistApi,` | `LikelistApi` | User Library | IProvableItemLikable.GetLikedProvidableIdsAsync(typeId: song) | L |
| `HyPlayer\Services\Downloads\DownloadManager.cs` | 269 | `var lyricResult = await _api.RequestAsync(NeteaseApis.LyricApi, lyricRequest);` | `LyricApi` | Lyrics | ILyricProvidable.GetLyricInfoAsync | L |
| `HyPlayer\Services\Downloads\DownloadManager.cs` | 408 | `var urlResult = await _api.RequestAsync(NeteaseApis.SongUrlApi, urlRequest);` | `SongUrlApi` | Stream URL | IMusicResourceProvidable.GetMusicResourceAsync | L |
| `HyPlayer\Services\History\HistoryManagement.cs` | 128 | `var result = await Ioc.Default.GetRequiredService<NeteaseCloudMusicApiHandler>().RequestAsync(NeteaseApis.SongDetailApi,` | `SongDetailApi` | Song/Album/Artist/Playlist details | IProvidableItemProvidable.GetProvidableItemByIdAsync / IProvidableItemRangeProvidable.GetProvidableItemsRangeAsync | M |
| `HyPlayer\Services\History\HistoryManagement.cs` | 170 | `var json = await Ioc.Default.GetRequiredService<NeteaseCloudMusicApiHandler>()?.RequestAsync(NeteaseApis.SongDetailApi,` | `SongDetailApi` | Song/Album/Artist/Playlist details | IProvidableItemProvidable.GetProvidableItemByIdAsync / IProvidableItemRangeProvidable.GetProvidableItemsRangeAsync | M |
| `HyPlayer\Services\Playback\LyricService.cs` | 213 | `var resp = await _api.RequestAsync(NeteaseApis.LyricApi, new LyricRequest { Id = item.Id });` | `LyricApi` | Lyrics | ILyricProvidable.GetLyricInfoAsync | L |
| `HyPlayer\Services\Playback\MediaProviders\CachedNeteaseProvider.cs` | 118 | `NeteaseApis.SongUrlApi, songRequest);` | `SongUrlApi` | Stream URL | IMusicResourceProvidable.GetMusicResourceAsync | L |
| `HyPlayer\Services\Playback\MediaProviders\NeteaseStreamingProvider.cs` | 85 | `NeteaseApis.SongUrlApi, songRequest);` | `SongUrlApi` | Stream URL | IMusicResourceProvidable.GetMusicResourceAsync | L |
| `HyPlayer\Services\Playback\QueueProviders\AlbumQueueSourceProvider.cs` | 40 | `var json = await _api.RequestAsync(NeteaseApis.AlbumApi,` | `AlbumApi` | Song/Album/Artist/Playlist details | IProvidableItemProvidable.GetProvidableItemByIdAsync | M |
| `HyPlayer\Services\Playback\QueueProviders\PlaylistQueueSourceProvider.cs` | 42 | `var detailResponse = await _api.RequestAsync(NeteaseApis.PlaylistTracksGetApi,` | `PlaylistTracksGetApi` | Song/Album/Artist/Playlist details | IProvidableItemProvidable/GetProvidableItemsRangeAsync for playlist contents | M |
| `HyPlayer\Services\Playback\QueueProviders\PlaylistQueueSourceProvider.cs` | 63 | `var songResponse = await _api.RequestAsync(NeteaseApis.SongDetailApi,` | `SongDetailApi` | Song/Album/Artist/Playlist details | IProvidableItemProvidable.GetProvidableItemByIdAsync / IProvidableItemRangeProvidable.GetProvidableItemsRangeAsync | M |
| `HyPlayer\Services\Playback\QueueProviders\RadioQueueSourceProvider.cs` | 47 | `var json = await _api.RequestAsync(NeteaseApis.DjChannelProgramsApi,` | `DjChannelProgramsApi` | Song/Album/Artist/Playlist details | IProvidableItemRangeProvidable or NEEDS NEW CONTRACT for radio programs | M |
| `HyPlayer\Services\Playback\QueueProviders\SingerHotQueueSourceProvider.cs` | 40 | `var j1 = await _api.RequestAsync(NeteaseApis.ArtistTopSongApi,` | `ArtistTopSongApi` | Song/Album/Artist/Playlist details | IProvidableItemProvidable.GetProvidableItemByIdAsync or IProvidableItemRangeProvidable for artist songs | M |
| `HyPlayer\Services\Playback\QueueProviders\SingleSongQueueSourceProvider.cs` | 41 | `var result = await _api.RequestAsync(NeteaseApis.SongDetailApi,` | `SongDetailApi` | Song/Album/Artist/Playlist details | IProvidableItemProvidable.GetProvidableItemByIdAsync / IProvidableItemRangeProvidable.GetProvidableItemsRangeAsync | M |
| `HyPlayer\Services\Playback\Strategies\PersonalFmStrategy.cs` | 89 | `var result = await _api.RequestAsync(NeteaseApis.PersonalFmApi, ct).ConfigureAwait(false);` | `PersonalFmApi` | Personal FM | IRecommendationProvidable or NEEDS NEW CONTRACT: stateful Personal FM stream | M |
| `HyPlayer\Services\Playback\Strategies\PersonalFmStrategy.cs` | 103 | `var result = await _api.RequestAsync(NeteaseApis.AiDjContentRcmdInfoApi,` | `AiDjContentRcmdInfoApi` | Personal FM | NEEDS NEW CONTRACT: AI DJ / Personal FM metadata stream | M |
| `HyPlayer\Shell\Navigation\NavigationShellViewModel.cs` | 194 | `var json = await _api.RequestAsync(NeteaseApis.UserPlaylistApi,` | `UserPlaylistApi` | User Library | NEEDS NEW CONTRACT: user library playlist enumeration | H |
| `HyPlayer\Shell\Search\ShellSearchViewModel.cs` | 30 | `var json = await _api.RequestAsync(NeteaseApis.SearchSuggestionApi,` | `SearchSuggestionApi` | Search | NEEDS NEW CONTRACT: search suggestion provider | M |
| `HyPlayer\UI\Controls\SingleComment.xaml.cs` | 88 | `var rst = await Ioc.Default.GetRequiredService<NeteaseCloudMusicApiHandler>()!.RequestAsync(NeteaseApis.CommentFloorApi,` | `CommentFloorApi` | Comment operations | NEEDS NEW CONTRACT: threaded/floor comments provider | M |
| `HyPlayer\UI\Controls\SingleComment.xaml.cs` | 123 | `var result = await Ioc.Default.GetRequiredService<NeteaseCloudMusicApiHandler>().RequestAsync(NeteaseApis.CommentLikeApi,` | `CommentLikeApi` | Comment operations | NEEDS NEW CONTRACT: comment like/unlike | M |
| `HyPlayer\UI\Dialogs\CreateSonglistDialog.xaml.cs` | 30 | `var result = await Ioc.Default.GetRequiredService<NeteaseCloudMusicApiHandler>().RequestAsync(NeteaseApis.PlaylistCreateApi,` | `PlaylistCreateApi` | Create/Delete/Privacy playlist | NEEDS NEW CONTRACT: playlist management create | H |
| `HyPlayer\UI\Dialogs\SongListSelectDialog.xaml.cs` | 28 | `await Ioc.Default.GetRequiredService<NeteaseCloudMusicApiHandler>()?.RequestAsync(NeteaseApis.PlaylistTracksEditApi,` | `PlaylistTracksEditApi` | Add/Remove playlist item | IProvableItemLikable.LikeProvidableItemAsync / UnlikeProvidableItemAsync (targetId = playlist id) | M |
| `HyPlayer\UI\Lists\PlaylistItem.xaml.cs` | 67 | `var result = await _api.RequestAsync(NeteaseApis.PlaylistPrivacyApi,` | `PlaylistPrivacyApi` | Create/Delete/Privacy playlist | NEEDS NEW CONTRACT: playlist privacy management | H |
| `HyPlayer\UI\Lists\PlaylistItem.xaml.cs` | 85 | `var result = await _api.RequestAsync(NeteaseApis.PlaylistDeleteApi,` | `PlaylistDeleteApi` | Create/Delete/Privacy playlist | NEEDS NEW CONTRACT: playlist management delete | H |
| `HyPlayer\UI\Lists\SongsList.xaml.cs` | 377 | `await _api.RequestAsync(NeteaseApis.PlaylistTracksEditApi,` | `PlaylistTracksEditApi` | Add/Remove playlist item | IProvableItemLikable.LikeProvidableItemAsync / UnlikeProvidableItemAsync (targetId = playlist id) | M |
| `HyPlayer\UI\Lists\SongsList.xaml.cs` | 387 | `await _api.RequestAsync(NeteaseApis.CloudDeleteApi,` | `CloudDeleteApi` | User Library | NEEDS NEW CONTRACT: cloud library delete | M |
| `HyPlayer\UI\Playback\PlayBar\PlayBar.xaml.cs` | 437 | `_taskRunner.Forget(_api.RequestAsync(NeteaseApis.PersonalFmTrashApi,` | `PersonalFmTrashApi` | Player trash/feedback | NEEDS NEW CONTRACT: provider playback feedback/trash | M |

## API Grep Match Inventory

Every non-generated grep hit for `NeteaseApi|NeteaseApis|NeteaseCloudMusicApiHandler` is listed below. Rows without `NeteaseApis.*` are imports, handler injection/registration, cookie/option access, JSON context coupling, or domain model coupling rather than endpoint operations.

| File | Line | Context | Classification |
|---|---:|---|---|
| `HyPlayer\App.xaml.cs` | 10 | `using HyPlayer.NeteaseApi;` | Coupling/import/handler/session/serialization reference |
| `HyPlayer\App.xaml.cs` | 108 | `var api = Ioc.Default.GetRequiredService<NeteaseCloudMusicApiHandler>();` | Coupling/import/handler/session/serialization reference |
| `HyPlayer\App.xaml.cs` | 124 | `var handler = NeteaseCloudMusicApiHandler.HttpClientHandler;` | Coupling/import/handler/session/serialization reference |
| `HyPlayer\App.xaml.cs` | 128 | `serviceCollection.AddSingleton(new NeteaseCloudMusicApiHandler(client));` | Coupling/import/handler/session/serialization reference |
| `HyPlayer\Classes\JsonDefaultContext.cs` | 3 | `using HyPlayer.NeteaseApi;` | Coupling/import/handler/session/serialization reference |
| `HyPlayer\Domain\Comments\Comment.cs` | 3 | `using HyPlayer.NeteaseApi.Models;` | Coupling/import/handler/session/serialization reference |
| `HyPlayer\Domain\Comments\CommentTarget.cs` | 1 | `using HyPlayer.NeteaseApi.Models;` | Coupling/import/handler/session/serialization reference |
| `HyPlayer\Domain\Settings\ApiSettings.cs` | 3 | `using HyPlayer.NeteaseApi;` | Coupling/import/handler/session/serialization reference |
| `HyPlayer\Domain\Settings\ApiSettings.cs` | 68 | `Ioc.Default.GetRequiredService<NeteaseCloudMusicApiHandler>()?.Option.FakeCheckToken = value;` | Coupling/import/handler/session/serialization reference |
| `HyPlayer\Domain\Settings\Settings.cs` | 2 | `using HyPlayer.NeteaseApi;` | Coupling/import/handler/session/serialization reference |
| `HyPlayer\Domain\Settings\Settings.cs` | 302 | `foreach (var item in Ioc.Default.GetRequiredService<NeteaseCloudMusicApiHandler>().Option.Cookies)` | Coupling/import/handler/session/serialization reference |
| `HyPlayer\Domain\Settings\Settings.cs` | 321 | `Ioc.Default.GetRequiredService<NeteaseCloudMusicApiHandler>().Option.Cookies.Add(item.Key, (string)item.Value);` | Coupling/import/handler/session/serialization reference |
| `HyPlayer\Features\Album\AlbumPageViewModel.cs` | 9 | `using HyPlayer.NeteaseApi;` | Coupling/import/handler/session/serialization reference |
| `HyPlayer\Features\Album\AlbumPageViewModel.cs` | 10 | `using HyPlayer.NeteaseApi.ApiContracts;` | Coupling/import/handler/session/serialization reference |
| `HyPlayer\Features\Album\AlbumPageViewModel.cs` | 11 | `using HyPlayer.NeteaseApi.ApiContracts.Album;` | Coupling/import/handler/session/serialization reference |
| `HyPlayer\Features\Album\AlbumPageViewModel.cs` | 27 | `private readonly NeteaseCloudMusicApiHandler _api;` | Coupling/import/handler/session/serialization reference |
| `HyPlayer\Features\Album\AlbumPageViewModel.cs` | 36 | `NeteaseCloudMusicApiHandler api,` | Coupling/import/handler/session/serialization reference |
| `HyPlayer\Features\Album\AlbumPageViewModel.cs` | 75 | `var json = await _api.RequestAsync(NeteaseApis.AlbumDetailDynamicApi,` | Direct endpoint operation |
| `HyPlayer\Features\Album\AlbumPageViewModel.cs` | 94 | `var json = await _api.RequestAsync(NeteaseApis.AlbumApi,` | Direct endpoint operation |
| `HyPlayer\Features\Album\AlbumPageViewModel.cs` | 187 | `_taskRunner.Forget(_api.RequestAsync(NeteaseApis.AlbumSubscribeApi,` | Direct endpoint operation |
| `HyPlayer\Features\Artist\ArtistPageViewModel.cs` | 9 | `using HyPlayer.NeteaseApi;` | Coupling/import/handler/session/serialization reference |
| `HyPlayer\Features\Artist\ArtistPageViewModel.cs` | 10 | `using HyPlayer.NeteaseApi.ApiContracts;` | Coupling/import/handler/session/serialization reference |
| `HyPlayer\Features\Artist\ArtistPageViewModel.cs` | 11 | `using HyPlayer.NeteaseApi.ApiContracts.Artist;` | Coupling/import/handler/session/serialization reference |
| `HyPlayer\Features\Artist\ArtistPageViewModel.cs` | 12 | `using HyPlayer.NeteaseApi.ApiContracts.Song;` | Coupling/import/handler/session/serialization reference |
| `HyPlayer\Features\Artist\ArtistPageViewModel.cs` | 25 | `private readonly NeteaseCloudMusicApiHandler _api;` | Coupling/import/handler/session/serialization reference |
| `HyPlayer\Features\Artist\ArtistPageViewModel.cs` | 30 | `NeteaseCloudMusicApiHandler api,` | Coupling/import/handler/session/serialization reference |
| `HyPlayer\Features\Artist\ArtistPageViewModel.cs` | 64 | `var resp = await _api.RequestAsync(NeteaseApis.ArtistDetailApi,` | Direct endpoint operation |
| `HyPlayer\Features\Artist\ArtistPageViewModel.cs` | 106 | `var j1res = await _api.RequestAsync(NeteaseApis.ArtistTopSongApi,` | Direct endpoint operation |
| `HyPlayer\Features\Artist\ArtistPageViewModel.cs` | 119 | `var json = await _api.RequestAsync(NeteaseApis.SongDetailApi,` | Direct endpoint operation |
| `HyPlayer\Features\Artist\ArtistPageViewModel.cs` | 147 | `var resp = await _api.RequestAsync(NeteaseApis.ArtistSongsApi,` | Direct endpoint operation |
| `HyPlayer\Features\Artist\ArtistPageViewModel.cs` | 175 | `var resp = await _api.RequestAsync(NeteaseApis.ArtistAlbumsApi,` | Direct endpoint operation |
| `HyPlayer\Features\Comments\Comments.xaml.cs` | 6 | `using HyPlayer.NeteaseApi;` | Coupling/import/handler/session/serialization reference |
| `HyPlayer\Features\Comments\Comments.xaml.cs` | 7 | `using HyPlayer.NeteaseApi.ApiContracts;` | Coupling/import/handler/session/serialization reference |
| `HyPlayer\Features\Comments\Comments.xaml.cs` | 8 | `using HyPlayer.NeteaseApi.ApiContracts.Comment;` | Coupling/import/handler/session/serialization reference |
| `HyPlayer\Features\Comments\Comments.xaml.cs` | 9 | `using HyPlayer.NeteaseApi.Models;` | Coupling/import/handler/session/serialization reference |
| `HyPlayer\Features\Comments\Comments.xaml.cs` | 34 | `private readonly NeteaseCloudMusicApiHandler _api = Ioc.Default.GetRequiredService<NeteaseCloudMusicApiHandler>();` | Coupling/import/handler/session/serialization reference |
| `HyPlayer\Features\Comments\Comments.xaml.cs` | 117 | `var result = await _api.RequestAsync(NeteaseApis.CommentsApi, new CommentsRequest` | Direct endpoint operation |
| `HyPlayer\Features\Home\HomePage.xaml.cs` | 5 | `using HyPlayer.NeteaseApi;` | Coupling/import/handler/session/serialization reference |
| `HyPlayer\Features\Home\HomePage.xaml.cs` | 6 | `using HyPlayer.NeteaseApi.ApiContracts;` | Coupling/import/handler/session/serialization reference |
| `HyPlayer\Features\Home\HomePage.xaml.cs` | 7 | `using HyPlayer.NeteaseApi.ApiContracts.Playlist;` | Coupling/import/handler/session/serialization reference |
| `HyPlayer\Features\Home\HomePage.xaml.cs` | 21 | `private readonly NeteaseCloudMusicApiHandler _api = Ioc.Default.GetRequiredService<NeteaseCloudMusicApiHandler>();` | Coupling/import/handler/session/serialization reference |
| `HyPlayer\Features\Home\HomePage.xaml.cs` | 57 | `var result = await _api.RequestAsync(NeteaseApis.PlaylistPrivacyApi,` | Direct endpoint operation |
| `HyPlayer\Features\Home\HomePage.xaml.cs` | 76 | `var result = await _api.RequestAsync(NeteaseApis.PlaylistDeleteApi,` | Direct endpoint operation |
| `HyPlayer\Features\Home\HomeViewModel.cs` | 7 | `using HyPlayer.NeteaseApi;` | Coupling/import/handler/session/serialization reference |
| `HyPlayer\Features\Home\HomeViewModel.cs` | 8 | `using HyPlayer.NeteaseApi.ApiContracts;` | Coupling/import/handler/session/serialization reference |
| `HyPlayer\Features\Home\HomeViewModel.cs` | 19 | `private NeteaseCloudMusicApiHandler _neteaseApi;` | Coupling/import/handler/session/serialization reference |
| `HyPlayer\Features\Home\HomeViewModel.cs` | 32 | `public HomeViewModel(NeteaseCloudMusicApiHandler neteaseApi, IPlaylistService playlist, INavigationService navigation)` | Coupling/import/handler/session/serialization reference |
| `HyPlayer\Features\Home\HomeViewModel.cs` | 41 | `var rcmdListResult = await _neteaseApi.RequestAsync(NeteaseApis.RecommendPlaylistsApi);` | Direct endpoint operation |
| `HyPlayer\Features\Home\HomeViewModel.cs` | 42 | `var topListResult = await _neteaseApi.RequestAsync(NeteaseApis.ToplistApi);` | Direct endpoint operation |
| `HyPlayer\Features\Home\HomeViewModel.cs` | 43 | `var categoryListResult = await _neteaseApi.RequestAsync(NeteaseApis.PlaylistCategoryListApi);` | Direct endpoint operation |
| `HyPlayer\Features\Home\HomeViewModel.cs` | 44 | `var rcmdSongsResult = await _neteaseApi.RequestAsync(NeteaseApis.RecommendSongsApi);` | Direct endpoint operation |
| `HyPlayer\Features\Library\FavoriteViewModel.cs` | 6 | `using HyPlayer.NeteaseApi;` | Coupling/import/handler/session/serialization reference |
| `HyPlayer\Features\Library\FavoriteViewModel.cs` | 7 | `using HyPlayer.NeteaseApi.ApiContracts;` | Coupling/import/handler/session/serialization reference |
| `HyPlayer\Features\Library\FavoriteViewModel.cs` | 8 | `using HyPlayer.NeteaseApi.ApiContracts.Album;` | Coupling/import/handler/session/serialization reference |
| `HyPlayer\Features\Library\FavoriteViewModel.cs` | 9 | `using HyPlayer.NeteaseApi.ApiContracts.Artist;` | Coupling/import/handler/session/serialization reference |
| `HyPlayer\Features\Library\FavoriteViewModel.cs` | 22 | `private readonly NeteaseCloudMusicApiHandler _api;` | Coupling/import/handler/session/serialization reference |
| `HyPlayer\Features\Library\FavoriteViewModel.cs` | 26 | `NeteaseCloudMusicApiHandler api,` | Coupling/import/handler/session/serialization reference |
| `HyPlayer\Features\Library\FavoriteViewModel.cs` | 61 | `var json = await _api.RequestAsync(NeteaseApis.DjChannelSubscribedApi);` | Direct endpoint operation |
| `HyPlayer\Features\Library\FavoriteViewModel.cs` | 97 | `var json = await _api.RequestAsync(NeteaseApis.ArtistSublistApi,` | Direct endpoint operation |
| `HyPlayer\Features\Library\FavoriteViewModel.cs` | 135 | `var jv = await _api!.RequestAsync(NeteaseApis.AlbumSublistApi,` | Direct endpoint operation |
| `HyPlayer\Features\Library\HIstoryPage.xaml.cs` | 6 | `using HyPlayer.NeteaseApi;` | Coupling/import/handler/session/serialization reference |
| `HyPlayer\Features\Library\HIstoryPage.xaml.cs` | 7 | `using HyPlayer.NeteaseApi.ApiContracts;` | Coupling/import/handler/session/serialization reference |
| `HyPlayer\Features\Library\HIstoryPage.xaml.cs` | 8 | `using HyPlayer.NeteaseApi.ApiContracts.User;` | Coupling/import/handler/session/serialization reference |
| `HyPlayer\Features\Library\HIstoryPage.xaml.cs` | 9 | `using HyPlayer.NeteaseApi.Bases;` | Coupling/import/handler/session/serialization reference |
| `HyPlayer\Features\Library\HIstoryPage.xaml.cs` | 31 | `private readonly NeteaseCloudMusicApiHandler _api = Ioc.Default.GetRequiredService<NeteaseCloudMusicApiHandler>();` | Coupling/import/handler/session/serialization reference |
| `HyPlayer\Features\Library\HIstoryPage.xaml.cs` | 106 | `var response = await _api.RequestAsync<UserRecordAllResponse, UserRecordRequest, UserRecordResponse, ErrorResultBase, UserRecordActualRequest>(NeteaseApis.UserRecordApi,` | Direct endpoint operation |
| `HyPlayer\Features\Library\HIstoryPage.xaml.cs` | 116 | `var response = await _api.RequestAsync<UserRecordWeekResponse, UserRecordRequest, UserRecordResponse, ErrorResultBase, UserRecordActualRequest>(NeteaseApis.UserRecordApi,` | Direct endpoint operation |
| `HyPlayer\Features\Library\MusicCloudPage.xaml.cs` | 8 | `using HyPlayer.NeteaseApi;` | Coupling/import/handler/session/serialization reference |
| `HyPlayer\Features\Library\MusicCloudPage.xaml.cs` | 9 | `using HyPlayer.NeteaseApi.ApiContracts;` | Coupling/import/handler/session/serialization reference |
| `HyPlayer\Features\Library\MusicCloudPage.xaml.cs` | 10 | `using HyPlayer.NeteaseApi.ApiContracts.Cloud;` | Coupling/import/handler/session/serialization reference |
| `HyPlayer\Features\Library\MusicCloudPage.xaml.cs` | 35 | `private readonly NeteaseCloudMusicApiHandler _api = Ioc.Default.GetRequiredService<NeteaseCloudMusicApiHandler>();` | Coupling/import/handler/session/serialization reference |
| `HyPlayer\Features\Library\MusicCloudPage.xaml.cs` | 63 | `var json = await _api.RequestAsync(NeteaseApis.CloudGetApi,` | Direct endpoint operation |
| `HyPlayer\Features\Playlist\SongListViewModel.cs` | 12 | `using HyPlayer.NeteaseApi;` | Coupling/import/handler/session/serialization reference |
| `HyPlayer\Features\Playlist\SongListViewModel.cs` | 13 | `using HyPlayer.NeteaseApi.ApiContracts;` | Coupling/import/handler/session/serialization reference |
| `HyPlayer\Features\Playlist\SongListViewModel.cs` | 14 | `using HyPlayer.NeteaseApi.ApiContracts.Playlist;` | Coupling/import/handler/session/serialization reference |
| `HyPlayer\Features\Playlist\SongListViewModel.cs` | 15 | `using HyPlayer.NeteaseApi.ApiContracts.Song;` | Coupling/import/handler/session/serialization reference |
| `HyPlayer\Features\Playlist\SongListViewModel.cs` | 35 | `private readonly NeteaseCloudMusicApiHandler _api;` | Coupling/import/handler/session/serialization reference |
| `HyPlayer\Features\Playlist\SongListViewModel.cs` | 47 | `NeteaseCloudMusicApiHandler api,` | Coupling/import/handler/session/serialization reference |
| `HyPlayer\Features\Playlist\SongListViewModel.cs` | 109 | `var json = await _api.RequestAsync(NeteaseApis.PlaylistDetailApi,` | Direct endpoint operation |
| `HyPlayer\Features\Playlist\SongListViewModel.cs` | 177 | `var json = await _api.RequestAsync(NeteaseApis.RecommendSongsApi);` | Direct endpoint operation |
| `HyPlayer\Features\Playlist\SongListViewModel.cs` | 208 | `var rst = await _api.RequestAsync(NeteaseApis.PlaylistTracksGetApi,` | Direct endpoint operation |
| `HyPlayer\Features\Playlist\SongListViewModel.cs` | 243 | `var json = await _api.RequestAsync(NeteaseApis.SongDetailApi,` | Direct endpoint operation |
| `HyPlayer\Features\Playlist\SongListViewModel.cs` | 386 | `var result = await _api.RequestAsync(NeteaseApis.PlaylistSubscribeApi,` | Direct endpoint operation |
| `HyPlayer\Features\Radio\RadioPage.xaml.cs` | 9 | `using HyPlayer.NeteaseApi;` | Coupling/import/handler/session/serialization reference |
| `HyPlayer\Features\Radio\RadioPage.xaml.cs` | 10 | `using HyPlayer.NeteaseApi.ApiContracts;` | Coupling/import/handler/session/serialization reference |
| `HyPlayer\Features\Radio\RadioPage.xaml.cs` | 11 | `using HyPlayer.NeteaseApi.ApiContracts.DjChannel;` | Coupling/import/handler/session/serialization reference |
| `HyPlayer\Features\Radio\RadioPage.xaml.cs` | 34 | `private readonly NeteaseCloudMusicApiHandler _api = Ioc.Default.GetRequiredService<NeteaseCloudMusicApiHandler>();` | Coupling/import/handler/session/serialization reference |
| `HyPlayer\Features\Radio\RadioPage.xaml.cs` | 90 | `var rest = await _api.RequestAsync(NeteaseApis.DjChannelProgramsApi,` | Direct endpoint operation |
| `HyPlayer\Features\Radio\RadioPage.xaml.cs` | 133 | `var json = await _api.RequestAsync(NeteaseApis.DjChannelDetailApi,` | Direct endpoint operation |
| `HyPlayer\Features\Radio\RadioPage.xaml.cs` | 253 | `var rest = await _api.RequestAsync(NeteaseApis.DjChannelProgramsApi,` | Direct endpoint operation |
| `HyPlayer\Features\Search\Search.xaml.cs` | 8 | `using HyPlayer.NeteaseApi;` | Coupling/import/handler/session/serialization reference |
| `HyPlayer\Features\Search\Search.xaml.cs` | 9 | `using HyPlayer.NeteaseApi.ApiContracts;` | Coupling/import/handler/session/serialization reference |
| `HyPlayer\Features\Search\Search.xaml.cs` | 10 | `using HyPlayer.NeteaseApi.ApiContracts.Recommend;` | Coupling/import/handler/session/serialization reference |
| `HyPlayer\Features\Search\Search.xaml.cs` | 11 | `using HyPlayer.NeteaseApi.Bases;` | Coupling/import/handler/session/serialization reference |
| `HyPlayer\Features\Search\Search.xaml.cs` | 12 | `using HyPlayer.NeteaseApi.Models;` | Coupling/import/handler/session/serialization reference |
| `HyPlayer\Features\Search\Search.xaml.cs` | 37 | `private readonly NeteaseCloudMusicApiHandler _api = Ioc.Default.GetRequiredService<NeteaseCloudMusicApiHandler>();` | Coupling/import/handler/session/serialization reference |
| `HyPlayer\Features\Search\Search.xaml.cs` | 161 | `SearchRequest, SearchResponse, ErrorResultBase, SearchActualRequest>(NeteaseApis.SearchApi,` | Direct endpoint operation |
| `HyPlayer\Features\Search\Search.xaml.cs` | 213 | `SearchRequest, SearchResponse, ErrorResultBase, SearchActualRequest>(NeteaseApis.SearchApi,` | Direct endpoint operation |
| `HyPlayer\Features\Search\Search.xaml.cs` | 266 | `SearchRequest, SearchResponse, ErrorResultBase, SearchActualRequest>(NeteaseApis.SearchApi,` | Direct endpoint operation |
| `HyPlayer\Features\Search\Search.xaml.cs` | 319 | `SearchRequest, SearchResponse, ErrorResultBase, SearchActualRequest>(NeteaseApis.SearchApi,` | Direct endpoint operation |
| `HyPlayer\Features\Search\Search.xaml.cs` | 372 | `SearchRequest, SearchResponse, ErrorResultBase, SearchActualRequest>(NeteaseApis.SearchApi,` | Direct endpoint operation |
| `HyPlayer\Features\Search\Search.xaml.cs` | 422 | `SearchRequest, SearchResponse, ErrorResultBase, SearchActualRequest>(NeteaseApis.SearchApi,` | Direct endpoint operation |
| `HyPlayer\Features\Search\Search.xaml.cs` | 477 | `SearchRequest, SearchResponse, ErrorResultBase, SearchActualRequest>(NeteaseApis.SearchApi,` | Direct endpoint operation |
| `HyPlayer\Features\Search\Search.xaml.cs` | 526 | `SearchRequest, SearchResponse, ErrorResultBase, SearchActualRequest>(NeteaseApis.SearchApi,` | Direct endpoint operation |
| `HyPlayer\Features\Search\Search.xaml.cs` | 577 | `SearchRequest, SearchResponse, ErrorResultBase, SearchActualRequest>(NeteaseApis.SearchApi,` | Direct endpoint operation |
| `HyPlayer\Features\Search\Search.xaml.cs` | 674 | `var json = await _api.RequestAsync(NeteaseApis.SearchSuggestionApi,` | Direct endpoint operation |
| `HyPlayer\Features\Settings\Settings.xaml.cs` | 6 | `using HyPlayer.NeteaseApi;` | Coupling/import/handler/session/serialization reference |
| `HyPlayer\Features\Settings\Settings.xaml.cs` | 52 | `private readonly NeteaseCloudMusicApiHandler _api = Ioc.Default.GetRequiredService<NeteaseCloudMusicApiHandler>();` | Coupling/import/handler/session/serialization reference |
| `HyPlayer\Features\User\Me.xaml.cs` | 7 | `using HyPlayer.NeteaseApi;` | Coupling/import/handler/session/serialization reference |
| `HyPlayer\Features\User\Me.xaml.cs` | 26 | `private readonly NeteaseCloudMusicApiHandler _api = Ioc.Default.GetRequiredService<NeteaseCloudMusicApiHandler>();` | Coupling/import/handler/session/serialization reference |
| `HyPlayer\Features\User\MeViewModel.cs` | 7 | `using HyPlayer.NeteaseApi;` | Coupling/import/handler/session/serialization reference |
| `HyPlayer\Features\User\MeViewModel.cs` | 8 | `using HyPlayer.NeteaseApi.ApiContracts;` | Coupling/import/handler/session/serialization reference |
| `HyPlayer\Features\User\MeViewModel.cs` | 9 | `using HyPlayer.NeteaseApi.ApiContracts.User;` | Coupling/import/handler/session/serialization reference |
| `HyPlayer\Features\User\MeViewModel.cs` | 27 | `private NeteaseCloudMusicApiHandler _neteaseApi;` | Coupling/import/handler/session/serialization reference |
| `HyPlayer\Features\User\MeViewModel.cs` | 30 | `public MeViewModel(NeteaseCloudMusicApiHandler api, Setting settings, INotificationService notification)` | Coupling/import/handler/session/serialization reference |
| `HyPlayer\Features\User\MeViewModel.cs` | 40 | `var json = await _neteaseApi.RequestAsync(NeteaseApis.UserDetailApi,` | Direct endpoint operation |
| `HyPlayer\Features\User\MeViewModel.cs` | 63 | `var json = await _neteaseApi.RequestAsync(NeteaseApis.UserPlaylistApi,` | Direct endpoint operation |
| `HyPlayer\Features\Video\MVPage.xaml.cs` | 7 | `using HyPlayer.NeteaseApi;` | Coupling/import/handler/session/serialization reference |
| `HyPlayer\Features\Video\MVPage.xaml.cs` | 8 | `using HyPlayer.NeteaseApi.ApiContracts;` | Coupling/import/handler/session/serialization reference |
| `HyPlayer\Features\Video\MVPage.xaml.cs` | 9 | `using HyPlayer.NeteaseApi.ApiContracts.Video;` | Coupling/import/handler/session/serialization reference |
| `HyPlayer\Features\Video\MVPage.xaml.cs` | 31 | `private readonly NeteaseCloudMusicApiHandler _api = Ioc.Default.GetRequiredService<NeteaseCloudMusicApiHandler>();` | Coupling/import/handler/session/serialization reference |
| `HyPlayer\Features\Video\MVPage.xaml.cs` | 77 | `var json = await _api.RequestAsync(NeteaseApis.MlogRcmdFeedListApi,` | Direct endpoint operation |
| `HyPlayer\Features\Video\MVPage.xaml.cs` | 159 | `var json = await _api.RequestAsync(NeteaseApis.VideoUrlApi,` | Direct endpoint operation |
| `HyPlayer\Features\Video\MVPage.xaml.cs` | 175 | `var json = await _api.RequestAsync(NeteaseApis.MlogUrlApi,` | Direct endpoint operation |
| `HyPlayer\Features\Video\MVPage.xaml.cs` | 201 | `var json = await _api.RequestAsync(NeteaseApis.VideoDetailApi,` | Direct endpoint operation |
| `HyPlayer\Features\Video\MVPage.xaml.cs` | 226 | `var json = await _api.RequestAsync(NeteaseApis.MlogDetailApi,` | Direct endpoint operation |
| `HyPlayer\Infrastructure\Netease\Api.cs` | 5 | `using HyPlayer.NeteaseApi;` | Coupling/import/handler/session/serialization reference |
| `HyPlayer\Infrastructure\Netease\Api.cs` | 6 | `using HyPlayer.NeteaseApi.ApiContracts;` | Coupling/import/handler/session/serialization reference |
| `HyPlayer\Infrastructure\Netease\Api.cs` | 7 | `using HyPlayer.NeteaseApi.ApiContracts.Playlist;` | Coupling/import/handler/session/serialization reference |
| `HyPlayer\Infrastructure\Netease\Api.cs` | 8 | `using HyPlayer.NeteaseApi.ApiContracts.Song;` | Coupling/import/handler/session/serialization reference |
| `HyPlayer\Infrastructure\Netease\Api.cs` | 24 | `var api = Ioc.Default.GetRequiredService<NeteaseCloudMusicApiHandler>();` | Coupling/import/handler/session/serialization reference |
| `HyPlayer\Infrastructure\Netease\Api.cs` | 26 | `var requestResult = await api.RequestAsync(NeteaseApis.LikeApi,` | Direct endpoint operation |
| `HyPlayer\Infrastructure\Netease\Api.cs` | 41 | `var api = Ioc.Default.GetRequiredService<NeteaseCloudMusicApiHandler>();` | Coupling/import/handler/session/serialization reference |
| `HyPlayer\Infrastructure\Netease\Api.cs` | 49 | `var jsoon = await api.RequestAsync(NeteaseApis.PlaymodeIntelligenceListApi,` | Direct endpoint operation |
| `HyPlayer\Infrastructure\Netease\CloudUpload.cs` | 5 | `using HyPlayer.NeteaseApi;` | Coupling/import/handler/session/serialization reference |
| `HyPlayer\Infrastructure\Netease\CloudUpload.cs` | 6 | `using HyPlayer.NeteaseApi.ApiContracts;` | Coupling/import/handler/session/serialization reference |
| `HyPlayer\Infrastructure\Netease\CloudUpload.cs` | 7 | `using HyPlayer.NeteaseApi.ApiContracts.Cloud;` | Coupling/import/handler/session/serialization reference |
| `HyPlayer\Infrastructure\Netease\CloudUpload.cs` | 35 | `var api = Ioc.Default.GetRequiredService<NeteaseCloudMusicApiHandler>();` | Coupling/import/handler/session/serialization reference |
| `HyPlayer\Infrastructure\Netease\CloudUpload.cs` | 68 | `var checkResult = await api.RequestAsync(NeteaseApis.CloudUploadCheckApi,` | Direct endpoint operation |
| `HyPlayer\Infrastructure\Netease\CloudUpload.cs` | 102 | `var tokenRes = await api.RequestAsync(NeteaseApis.CloudUploadTokenAllocApi, tokenRequest);` | Direct endpoint operation |
| `HyPlayer\Infrastructure\Netease\CloudUpload.cs` | 115 | `var loadBalancerRes = await api.RequestAsync(NeteaseApis.NeteaseUploadLoadBalancerGetApi,` | Direct endpoint operation |
| `HyPlayer\Infrastructure\Netease\CloudUpload.cs` | 141 | `var coverAllocRes = await api.RequestAsync(NeteaseApis.CloudUploadCoverTokenAllocApi,` | Direct endpoint operation |
| `HyPlayer\Infrastructure\Netease\CloudUpload.cs` | 157 | `var imgloadBalancerRes = await api.RequestAsync(NeteaseApis.NeteaseUploadLoadBalancerGetApi,` | Direct endpoint operation |
| `HyPlayer\Infrastructure\Netease\CloudUpload.cs` | 171 | `var infoRes = await api.RequestAsync(NeteaseApis.CloudUploadInfoApi, infoReq);` | Direct endpoint operation |
| `HyPlayer\Infrastructure\Netease\CloudUpload.cs` | 181 | `var cloudPubRes = await api.RequestAsync(NeteaseApis.CloudPubApi, cloudPubReq);` | Direct endpoint operation |
| `HyPlayer\Infrastructure\Netease\ListenTogetherManager.cs` | 4 | `using HyPlayer.NeteaseApi;` | Coupling/import/handler/session/serialization reference |
| `HyPlayer\Infrastructure\Netease\ListenTogetherManager.cs` | 5 | `using HyPlayer.NeteaseApi.ApiContracts;` | Coupling/import/handler/session/serialization reference |
| `HyPlayer\Infrastructure\Netease\ListenTogetherManager.cs` | 6 | `using HyPlayer.NeteaseApi.ApiContracts.ListenTogether;` | Coupling/import/handler/session/serialization reference |
| `HyPlayer\Infrastructure\Netease\ListenTogetherManager.cs` | 7 | `using HyPlayer.NeteaseApi.ApiContracts.ListenTogether.Dual;` | Coupling/import/handler/session/serialization reference |
| `HyPlayer\Infrastructure\Netease\ListenTogetherManager.cs` | 30 | `private readonly NeteaseCloudMusicApiHandler _api;` | Coupling/import/handler/session/serialization reference |
| `HyPlayer\Infrastructure\Netease\ListenTogetherManager.cs` | 48 | `_api = Ioc.Default.GetRequiredService<NeteaseCloudMusicApiHandler>();` | Coupling/import/handler/session/serialization reference |
| `HyPlayer\Infrastructure\Netease\ListenTogetherManager.cs` | 72 | `var res = await mgr._api.RequestAsync(NeteaseApis.ListenTogetherRoomCreateApi,` | Direct endpoint operation |
| `HyPlayer\Infrastructure\Netease\ListenTogetherManager.cs` | 132 | `var api = Ioc.Default.GetRequiredService<NeteaseCloudMusicApiHandler>();` | Coupling/import/handler/session/serialization reference |
| `HyPlayer\Infrastructure\Netease\ListenTogetherManager.cs` | 133 | `var res = await api.RequestAsync(NeteaseApis.ListenTogetherRoomCheckApi,` | Direct endpoint operation |
| `HyPlayer\Infrastructure\Netease\ListenTogetherManager.cs` | 198 | `_ = _api.RequestAsync(NeteaseApis.ListenTogetherPlayCommandApi, req);` | Direct endpoint operation |
| `HyPlayer\Infrastructure\Netease\ListenTogetherManager.cs` | 221 | `_ = _api.RequestAsync(NeteaseApis.ListenTogetherPlayCommandApi, req);` | Direct endpoint operation |
| `HyPlayer\Infrastructure\Netease\ListenTogetherManager.cs` | 244 | `_ = _api.RequestAsync(NeteaseApis.ListenTogetherPlayCommandApi, req);` | Direct endpoint operation |
| `HyPlayer\Infrastructure\Netease\ListenTogetherManager.cs` | 257 | `_ = _api.RequestAsync(NeteaseApis.ListenTogetherPlayCommandApi,` | Direct endpoint operation |
| `HyPlayer\Infrastructure\Netease\ListenTogetherManager.cs` | 297 | `_ = _api.RequestAsync(NeteaseApis.ListenTogetherSyncListReportApi, req);` | Direct endpoint operation |
| `HyPlayer\Infrastructure\Netease\ListenTogetherManager.cs` | 314 | `var res = await _api.RequestAsync(NeteaseApis.ListenTogetherStatusApi,` | Direct endpoint operation |
| `HyPlayer\Infrastructure\Netease\Mapper.cs` | 3 | `using HyPlayer.NeteaseApi.ApiContracts.Artist;` | Coupling/import/handler/session/serialization reference |
| `HyPlayer\Infrastructure\Netease\Mapper.cs` | 4 | `using HyPlayer.NeteaseApi.ApiContracts.Recommend;` | Coupling/import/handler/session/serialization reference |
| `HyPlayer\Infrastructure\Netease\Mapper.cs` | 5 | `using HyPlayer.NeteaseApi.Models.ResponseModels;` | Coupling/import/handler/session/serialization reference |
| `HyPlayer\Infrastructure\Serialization\JsonDefaults.cs` | 2 | `using HyPlayer.NeteaseApi.ApiContracts;` | Coupling/import/handler/session/serialization reference |
| `HyPlayer\Infrastructure\Serialization\JsonDefaults.cs` | 3 | `using HyPlayer.NeteaseApi.Extensions.JsonSerializer;` | Coupling/import/handler/session/serialization reference |
| `HyPlayer\Infrastructure\Serialization\JsonDefaults.cs` | 19 | `TypeInfoResolver = JsonTypeInfoResolver.Combine(JsonDefaultContext.Default, NeteaseApiContractJsonContext.Default, LastFMJsonDefaultContext.Default)` | Coupling/import/handler/session/serialization reference |
| `HyPlayer\MainPage.xaml.cs` | 13 | `using HyPlayer.NeteaseApi;` | Coupling/import/handler/session/serialization reference |
| `HyPlayer\MainPage.xaml.cs` | 52 | `var api = Ioc.Default.GetRequiredService<NeteaseCloudMusicApiHandler>();` | Coupling/import/handler/session/serialization reference |
| `HyPlayer\Services\Authentication\AuthService.cs` | 5 | `using HyPlayer.NeteaseApi;` | Coupling/import/handler/session/serialization reference |
| `HyPlayer\Services\Authentication\AuthService.cs` | 6 | `using HyPlayer.NeteaseApi.ApiContracts;` | Coupling/import/handler/session/serialization reference |
| `HyPlayer\Services\Authentication\AuthService.cs` | 7 | `using HyPlayer.NeteaseApi.ApiContracts.Login;` | Coupling/import/handler/session/serialization reference |
| `HyPlayer\Services\Authentication\AuthService.cs` | 8 | `using HyPlayer.NeteaseApi.ApiContracts.Playlist;` | Coupling/import/handler/session/serialization reference |
| `HyPlayer\Services\Authentication\AuthService.cs` | 9 | `using HyPlayer.NeteaseApi.ApiContracts.Utils;` | Coupling/import/handler/session/serialization reference |
| `HyPlayer\Services\Authentication\AuthService.cs` | 32 | `private readonly NeteaseCloudMusicApiHandler _api;` | Coupling/import/handler/session/serialization reference |
| `HyPlayer\Services\Authentication\AuthService.cs` | 40 | `NeteaseCloudMusicApiHandler api,` | Coupling/import/handler/session/serialization reference |
| `HyPlayer\Services\Authentication\AuthService.cs` | 109 | `var response = await _api.RequestAsync(NeteaseApis.LoginCellphoneApi,` | Direct endpoint operation |
| `HyPlayer\Services\Authentication\AuthService.cs` | 121 | `var response = await _api.RequestAsync(NeteaseApis.LoginEmailApi,` | Direct endpoint operation |
| `HyPlayer\Services\Authentication\AuthService.cs` | 138 | `var key = await _api.RequestAsync(NeteaseApis.LoginQrCodeUnikeyApi, new LoginQrCodeUnikeyRequest());` | Direct endpoint operation |
| `HyPlayer\Services\Authentication\AuthService.cs` | 147 | `var res = await _api.RequestAsync(NeteaseApis.LoginQrCodeCheckApi,` | Direct endpoint operation |
| `HyPlayer\Services\Authentication\AuthService.cs` | 163 | `var rst = await _api.RequestAsync(NeteaseApis.LoginAnnounceDeviceApi, new LoginAnnounceDeviceRequest` | Direct endpoint operation |
| `HyPlayer\Services\Authentication\AuthService.cs` | 188 | `var statusResult = await _api.RequestAsync(NeteaseApis.LoginStatusApi);` | Direct endpoint operation |
| `HyPlayer\Services\Authentication\AuthService.cs` | 309 | `var js = await _api.RequestAsync(NeteaseApis.LikelistApi,` | Direct endpoint operation |
| `HyPlayer\Services\Downloads\DownloadManager.cs` | 11 | `using HyPlayer.NeteaseApi;` | Coupling/import/handler/session/serialization reference |
| `HyPlayer\Services\Downloads\DownloadManager.cs` | 12 | `using HyPlayer.NeteaseApi.ApiContracts;` | Coupling/import/handler/session/serialization reference |
| `HyPlayer\Services\Downloads\DownloadManager.cs` | 13 | `using HyPlayer.NeteaseApi.ApiContracts.Song;` | Coupling/import/handler/session/serialization reference |
| `HyPlayer\Services\Downloads\DownloadManager.cs` | 44 | `private readonly NeteaseCloudMusicApiHandler _api;` | Coupling/import/handler/session/serialization reference |
| `HyPlayer\Services\Downloads\DownloadManager.cs` | 79 | `NeteaseCloudMusicApiHandler api,` | Coupling/import/handler/session/serialization reference |
| `HyPlayer\Services\Downloads\DownloadManager.cs` | 269 | `var lyricResult = await _api.RequestAsync(NeteaseApis.LyricApi, lyricRequest);` | Direct endpoint operation |
| `HyPlayer\Services\Downloads\DownloadManager.cs` | 408 | `var urlResult = await _api.RequestAsync(NeteaseApis.SongUrlApi, urlRequest);` | Direct endpoint operation |
| `HyPlayer\Services\Downloads\DownloadManager.cs` | 489 | `private static NeteaseCloudMusicApiHandler Api => Ioc.Default.GetRequiredService<NeteaseCloudMusicApiHandler>();` | Coupling/import/handler/session/serialization reference |
| `HyPlayer\Services\History\HistoryManagement.cs` | 6 | `using HyPlayer.NeteaseApi;` | Coupling/import/handler/session/serialization reference |
| `HyPlayer\Services\History\HistoryManagement.cs` | 7 | `using HyPlayer.NeteaseApi.ApiContracts;` | Coupling/import/handler/session/serialization reference |
| `HyPlayer\Services\History\HistoryManagement.cs` | 8 | `using HyPlayer.NeteaseApi.ApiContracts.Song;` | Coupling/import/handler/session/serialization reference |
| `HyPlayer\Services\History\HistoryManagement.cs` | 128 | `var result = await Ioc.Default.GetRequiredService<NeteaseCloudMusicApiHandler>().RequestAsync(NeteaseApis.SongDetailApi,` | Direct endpoint operation |
| `HyPlayer\Services\History\HistoryManagement.cs` | 170 | `var json = await Ioc.Default.GetRequiredService<NeteaseCloudMusicApiHandler>()?.RequestAsync(NeteaseApis.SongDetailApi,` | Direct endpoint operation |
| `HyPlayer\Services\Playback\LyricService.cs` | 7 | `using HyPlayer.NeteaseApi;` | Coupling/import/handler/session/serialization reference |
| `HyPlayer\Services\Playback\LyricService.cs` | 8 | `using HyPlayer.NeteaseApi.ApiContracts;` | Coupling/import/handler/session/serialization reference |
| `HyPlayer\Services\Playback\LyricService.cs` | 9 | `using HyPlayer.NeteaseApi.ApiContracts.Song;` | Coupling/import/handler/session/serialization reference |
| `HyPlayer\Services\Playback\LyricService.cs` | 31 | `private readonly NeteaseCloudMusicApiHandler _api;` | Coupling/import/handler/session/serialization reference |
| `HyPlayer\Services\Playback\LyricService.cs` | 38 | `NeteaseCloudMusicApiHandler api,` | Coupling/import/handler/session/serialization reference |
| `HyPlayer\Services\Playback\LyricService.cs` | 213 | `var resp = await _api.RequestAsync(NeteaseApis.LyricApi, new LyricRequest { Id = item.Id });` | Direct endpoint operation |
| `HyPlayer\Services\Playback\MediaProviders\CachedNeteaseProvider.cs` | 6 | `using HyPlayer.NeteaseApi;` | Coupling/import/handler/session/serialization reference |
| `HyPlayer\Services\Playback\MediaProviders\CachedNeteaseProvider.cs` | 7 | `using HyPlayer.NeteaseApi.ApiContracts;` | Coupling/import/handler/session/serialization reference |
| `HyPlayer\Services\Playback\MediaProviders\CachedNeteaseProvider.cs` | 8 | `using HyPlayer.NeteaseApi.ApiContracts.Song;` | Coupling/import/handler/session/serialization reference |
| `HyPlayer\Services\Playback\MediaProviders\CachedNeteaseProvider.cs` | 38 | `private readonly NeteaseCloudMusicApiHandler _neteaseApi;` | Coupling/import/handler/session/serialization reference |
| `HyPlayer\Services\Playback\MediaProviders\CachedNeteaseProvider.cs` | 59 | `NeteaseCloudMusicApiHandler neteaseApi,` | Coupling/import/handler/session/serialization reference |
| `HyPlayer\Services\Playback\MediaProviders\CachedNeteaseProvider.cs` | 118 | `NeteaseApis.SongUrlApi, songRequest);` | Direct endpoint operation |
| `HyPlayer\Services\Playback\MediaProviders\NeteaseStreamingProvider.cs` | 6 | `using HyPlayer.NeteaseApi;` | Coupling/import/handler/session/serialization reference |
| `HyPlayer\Services\Playback\MediaProviders\NeteaseStreamingProvider.cs` | 7 | `using HyPlayer.NeteaseApi.ApiContracts;` | Coupling/import/handler/session/serialization reference |
| `HyPlayer\Services\Playback\MediaProviders\NeteaseStreamingProvider.cs` | 8 | `using HyPlayer.NeteaseApi.ApiContracts.Song;` | Coupling/import/handler/session/serialization reference |
| `HyPlayer\Services\Playback\MediaProviders\NeteaseStreamingProvider.cs` | 31 | `private readonly NeteaseCloudMusicApiHandler _neteaseApi;` | Coupling/import/handler/session/serialization reference |
| `HyPlayer\Services\Playback\MediaProviders\NeteaseStreamingProvider.cs` | 41 | `public NeteaseStreamingProvider(Setting setting, NeteaseCloudMusicApiHandler neteaseApi)` | Coupling/import/handler/session/serialization reference |
| `HyPlayer\Services\Playback\MediaProviders\NeteaseStreamingProvider.cs` | 85 | `NeteaseApis.SongUrlApi, songRequest);` | Direct endpoint operation |
| `HyPlayer\Services\Playback\QueueProviders\AlbumQueueSourceProvider.cs` | 3 | `using HyPlayer.NeteaseApi;` | Coupling/import/handler/session/serialization reference |
| `HyPlayer\Services\Playback\QueueProviders\AlbumQueueSourceProvider.cs` | 4 | `using HyPlayer.NeteaseApi.ApiContracts;` | Coupling/import/handler/session/serialization reference |
| `HyPlayer\Services\Playback\QueueProviders\AlbumQueueSourceProvider.cs` | 5 | `using HyPlayer.NeteaseApi.ApiContracts.Album;` | Coupling/import/handler/session/serialization reference |
| `HyPlayer\Services\Playback\QueueProviders\AlbumQueueSourceProvider.cs` | 21 | `private readonly NeteaseCloudMusicApiHandler _api;` | Coupling/import/handler/session/serialization reference |
| `HyPlayer\Services\Playback\QueueProviders\AlbumQueueSourceProvider.cs` | 24 | `public AlbumQueueSourceProvider(NeteaseCloudMusicApiHandler api, INotificationService notification)` | Coupling/import/handler/session/serialization reference |
| `HyPlayer\Services\Playback\QueueProviders\AlbumQueueSourceProvider.cs` | 40 | `var json = await _api.RequestAsync(NeteaseApis.AlbumApi,` | Direct endpoint operation |
| `HyPlayer\Services\Playback\QueueProviders\PlaylistQueueSourceProvider.cs` | 3 | `using HyPlayer.NeteaseApi;` | Coupling/import/handler/session/serialization reference |
| `HyPlayer\Services\Playback\QueueProviders\PlaylistQueueSourceProvider.cs` | 4 | `using HyPlayer.NeteaseApi.ApiContracts;` | Coupling/import/handler/session/serialization reference |
| `HyPlayer\Services\Playback\QueueProviders\PlaylistQueueSourceProvider.cs` | 5 | `using HyPlayer.NeteaseApi.ApiContracts.Playlist;` | Coupling/import/handler/session/serialization reference |
| `HyPlayer\Services\Playback\QueueProviders\PlaylistQueueSourceProvider.cs` | 6 | `using HyPlayer.NeteaseApi.ApiContracts.Song;` | Coupling/import/handler/session/serialization reference |
| `HyPlayer\Services\Playback\QueueProviders\PlaylistQueueSourceProvider.cs` | 23 | `private readonly NeteaseCloudMusicApiHandler _api;` | Coupling/import/handler/session/serialization reference |
| `HyPlayer\Services\Playback\QueueProviders\PlaylistQueueSourceProvider.cs` | 26 | `public PlaylistQueueSourceProvider(NeteaseCloudMusicApiHandler api, INotificationService notification)` | Coupling/import/handler/session/serialization reference |
| `HyPlayer\Services\Playback\QueueProviders\PlaylistQueueSourceProvider.cs` | 42 | `var detailResponse = await _api.RequestAsync(NeteaseApis.PlaylistTracksGetApi,` | Direct endpoint operation |
| `HyPlayer\Services\Playback\QueueProviders\PlaylistQueueSourceProvider.cs` | 63 | `var songResponse = await _api.RequestAsync(NeteaseApis.SongDetailApi,` | Direct endpoint operation |
| `HyPlayer\Services\Playback\QueueProviders\RadioQueueSourceProvider.cs` | 3 | `using HyPlayer.NeteaseApi;` | Coupling/import/handler/session/serialization reference |
| `HyPlayer\Services\Playback\QueueProviders\RadioQueueSourceProvider.cs` | 4 | `using HyPlayer.NeteaseApi.ApiContracts;` | Coupling/import/handler/session/serialization reference |
| `HyPlayer\Services\Playback\QueueProviders\RadioQueueSourceProvider.cs` | 5 | `using HyPlayer.NeteaseApi.ApiContracts.DjChannel;` | Coupling/import/handler/session/serialization reference |
| `HyPlayer\Services\Playback\QueueProviders\RadioQueueSourceProvider.cs` | 21 | `private readonly NeteaseCloudMusicApiHandler _api;` | Coupling/import/handler/session/serialization reference |
| `HyPlayer\Services\Playback\QueueProviders\RadioQueueSourceProvider.cs` | 24 | `public RadioQueueSourceProvider(NeteaseCloudMusicApiHandler api, INotificationService notification)` | Coupling/import/handler/session/serialization reference |
| `HyPlayer\Services\Playback\QueueProviders\RadioQueueSourceProvider.cs` | 47 | `var json = await _api.RequestAsync(NeteaseApis.DjChannelProgramsApi,` | Direct endpoint operation |
| `HyPlayer\Services\Playback\QueueProviders\SingerHotQueueSourceProvider.cs` | 3 | `using HyPlayer.NeteaseApi;` | Coupling/import/handler/session/serialization reference |
| `HyPlayer\Services\Playback\QueueProviders\SingerHotQueueSourceProvider.cs` | 4 | `using HyPlayer.NeteaseApi.ApiContracts;` | Coupling/import/handler/session/serialization reference |
| `HyPlayer\Services\Playback\QueueProviders\SingerHotQueueSourceProvider.cs` | 5 | `using HyPlayer.NeteaseApi.ApiContracts.Artist;` | Coupling/import/handler/session/serialization reference |
| `HyPlayer\Services\Playback\QueueProviders\SingerHotQueueSourceProvider.cs` | 21 | `private readonly NeteaseCloudMusicApiHandler _api;` | Coupling/import/handler/session/serialization reference |
| `HyPlayer\Services\Playback\QueueProviders\SingerHotQueueSourceProvider.cs` | 24 | `public SingerHotQueueSourceProvider(NeteaseCloudMusicApiHandler api, INotificationService notification)` | Coupling/import/handler/session/serialization reference |
| `HyPlayer\Services\Playback\QueueProviders\SingerHotQueueSourceProvider.cs` | 40 | `var j1 = await _api.RequestAsync(NeteaseApis.ArtistTopSongApi,` | Direct endpoint operation |
| `HyPlayer\Services\Playback\QueueProviders\SingleSongQueueSourceProvider.cs` | 3 | `using HyPlayer.NeteaseApi;` | Coupling/import/handler/session/serialization reference |
| `HyPlayer\Services\Playback\QueueProviders\SingleSongQueueSourceProvider.cs` | 4 | `using HyPlayer.NeteaseApi.ApiContracts;` | Coupling/import/handler/session/serialization reference |
| `HyPlayer\Services\Playback\QueueProviders\SingleSongQueueSourceProvider.cs` | 5 | `using HyPlayer.NeteaseApi.ApiContracts.Song;` | Coupling/import/handler/session/serialization reference |
| `HyPlayer\Services\Playback\QueueProviders\SingleSongQueueSourceProvider.cs` | 20 | `private readonly NeteaseCloudMusicApiHandler _api;` | Coupling/import/handler/session/serialization reference |
| `HyPlayer\Services\Playback\QueueProviders\SingleSongQueueSourceProvider.cs` | 23 | `public SingleSongQueueSourceProvider(NeteaseCloudMusicApiHandler api, INotificationService notification)` | Coupling/import/handler/session/serialization reference |
| `HyPlayer\Services\Playback\QueueProviders\SingleSongQueueSourceProvider.cs` | 41 | `var result = await _api.RequestAsync(NeteaseApis.SongDetailApi,` | Direct endpoint operation |
| `HyPlayer\Services\Playback\Strategies\PersonalFmStrategy.cs` | 4 | `using HyPlayer.NeteaseApi;` | Coupling/import/handler/session/serialization reference |
| `HyPlayer\Services\Playback\Strategies\PersonalFmStrategy.cs` | 5 | `using HyPlayer.NeteaseApi.ApiContracts;` | Coupling/import/handler/session/serialization reference |
| `HyPlayer\Services\Playback\Strategies\PersonalFmStrategy.cs` | 6 | `using HyPlayer.NeteaseApi.ApiContracts.PersonalFM;` | Coupling/import/handler/session/serialization reference |
| `HyPlayer\Services\Playback\Strategies\PersonalFmStrategy.cs` | 28 | `private readonly NeteaseCloudMusicApiHandler _api;` | Coupling/import/handler/session/serialization reference |
| `HyPlayer\Services\Playback\Strategies\PersonalFmStrategy.cs` | 37 | `public PersonalFmStrategy(NeteaseCloudMusicApiHandler api, Setting setting)` | Coupling/import/handler/session/serialization reference |
| `HyPlayer\Services\Playback\Strategies\PersonalFmStrategy.cs` | 89 | `var result = await _api.RequestAsync(NeteaseApis.PersonalFmApi, ct).ConfigureAwait(false);` | Direct endpoint operation |
| `HyPlayer\Services\Playback\Strategies\PersonalFmStrategy.cs` | 103 | `var result = await _api.RequestAsync(NeteaseApis.AiDjContentRcmdInfoApi,` | Direct endpoint operation |
| `HyPlayer\Shell\Navigation\NavigationShellViewModel.cs` | 6 | `using HyPlayer.NeteaseApi;` | Coupling/import/handler/session/serialization reference |
| `HyPlayer\Shell\Navigation\NavigationShellViewModel.cs` | 7 | `using HyPlayer.NeteaseApi.ApiContracts;` | Coupling/import/handler/session/serialization reference |
| `HyPlayer\Shell\Navigation\NavigationShellViewModel.cs` | 8 | `using HyPlayer.NeteaseApi.ApiContracts.User;` | Coupling/import/handler/session/serialization reference |
| `HyPlayer\Shell\Navigation\NavigationShellViewModel.cs` | 27 | `private readonly NeteaseCloudMusicApiHandler _api;` | Coupling/import/handler/session/serialization reference |
| `HyPlayer\Shell\Navigation\NavigationShellViewModel.cs` | 60 | `NeteaseCloudMusicApiHandler api,` | Coupling/import/handler/session/serialization reference |
| `HyPlayer\Shell\Navigation\NavigationShellViewModel.cs` | 194 | `var json = await _api.RequestAsync(NeteaseApis.UserPlaylistApi,` | Direct endpoint operation |
| `HyPlayer\Shell\Search\ShellSearchViewModel.cs` | 1 | `using HyPlayer.NeteaseApi;` | Coupling/import/handler/session/serialization reference |
| `HyPlayer\Shell\Search\ShellSearchViewModel.cs` | 2 | `using HyPlayer.NeteaseApi.ApiContracts;` | Coupling/import/handler/session/serialization reference |
| `HyPlayer\Shell\Search\ShellSearchViewModel.cs` | 3 | `using HyPlayer.NeteaseApi.ApiContracts.Recommend;` | Coupling/import/handler/session/serialization reference |
| `HyPlayer\Shell\Search\ShellSearchViewModel.cs` | 13 | `private readonly NeteaseCloudMusicApiHandler _api;` | Coupling/import/handler/session/serialization reference |
| `HyPlayer\Shell\Search\ShellSearchViewModel.cs` | 17 | `public ShellSearchViewModel(NeteaseCloudMusicApiHandler api,` | Coupling/import/handler/session/serialization reference |
| `HyPlayer\Shell\Search\ShellSearchViewModel.cs` | 30 | `var json = await _api.RequestAsync(NeteaseApis.SearchSuggestionApi,` | Direct endpoint operation |
| `HyPlayer\Shell\TestPage.xaml.cs` | 19 | `using HyPlayer.NeteaseApi;` | Coupling/import/handler/session/serialization reference |
| `HyPlayer\Shell\TestPage.xaml.cs` | 46 | `private readonly NeteaseCloudMusicApiHandler _api = Ioc.Default.GetRequiredService<NeteaseCloudMusicApiHandler>();` | Coupling/import/handler/session/serialization reference |
| `HyPlayer\UI\Controls\SingleComment.xaml.cs` | 8 | `using HyPlayer.NeteaseApi;` | Coupling/import/handler/session/serialization reference |
| `HyPlayer\UI\Controls\SingleComment.xaml.cs` | 9 | `using HyPlayer.NeteaseApi.ApiContracts;` | Coupling/import/handler/session/serialization reference |
| `HyPlayer\UI\Controls\SingleComment.xaml.cs` | 10 | `using HyPlayer.NeteaseApi.ApiContracts.Comment;` | Coupling/import/handler/session/serialization reference |
| `HyPlayer\UI\Controls\SingleComment.xaml.cs` | 88 | `var rst = await Ioc.Default.GetRequiredService<NeteaseCloudMusicApiHandler>()!.RequestAsync(NeteaseApis.CommentFloorApi,` | Direct endpoint operation |
| `HyPlayer\UI\Controls\SingleComment.xaml.cs` | 123 | `var result = await Ioc.Default.GetRequiredService<NeteaseCloudMusicApiHandler>().RequestAsync(NeteaseApis.CommentLikeApi,` | Direct endpoint operation |
| `HyPlayer\UI\Dialogs\CreateSonglistDialog.xaml.cs` | 4 | `using HyPlayer.NeteaseApi;` | Coupling/import/handler/session/serialization reference |
| `HyPlayer\UI\Dialogs\CreateSonglistDialog.xaml.cs` | 5 | `using HyPlayer.NeteaseApi.ApiContracts;` | Coupling/import/handler/session/serialization reference |
| `HyPlayer\UI\Dialogs\CreateSonglistDialog.xaml.cs` | 6 | `using HyPlayer.NeteaseApi.ApiContracts.Playlist;` | Coupling/import/handler/session/serialization reference |
| `HyPlayer\UI\Dialogs\CreateSonglistDialog.xaml.cs` | 26 | `string realIpBackup = Ioc.Default.GetRequiredService<NeteaseCloudMusicApiHandler>().Option.XRealIP;` | Coupling/import/handler/session/serialization reference |
| `HyPlayer\UI\Dialogs\CreateSonglistDialog.xaml.cs` | 28 | `Ioc.Default.GetRequiredService<NeteaseCloudMusicApiHandler>().Option.XRealIP = "118.88.88.88";` | Coupling/import/handler/session/serialization reference |
| `HyPlayer\UI\Dialogs\CreateSonglistDialog.xaml.cs` | 30 | `var result = await Ioc.Default.GetRequiredService<NeteaseCloudMusicApiHandler>().RequestAsync(NeteaseApis.PlaylistCreateApi,` | Direct endpoint operation |
| `HyPlayer\UI\Dialogs\CreateSonglistDialog.xaml.cs` | 43 | `Ioc.Default.GetRequiredService<NeteaseCloudMusicApiHandler>().Option.XRealIP = realIpBackup;// Restore user setting` | Coupling/import/handler/session/serialization reference |
| `HyPlayer\UI\Dialogs\SongListSelectDialog.xaml.cs` | 4 | `using HyPlayer.NeteaseApi;` | Coupling/import/handler/session/serialization reference |
| `HyPlayer\UI\Dialogs\SongListSelectDialog.xaml.cs` | 5 | `using HyPlayer.NeteaseApi.ApiContracts;` | Coupling/import/handler/session/serialization reference |
| `HyPlayer\UI\Dialogs\SongListSelectDialog.xaml.cs` | 6 | `using HyPlayer.NeteaseApi.ApiContracts.Playlist;` | Coupling/import/handler/session/serialization reference |
| `HyPlayer\UI\Dialogs\SongListSelectDialog.xaml.cs` | 28 | `await Ioc.Default.GetRequiredService<NeteaseCloudMusicApiHandler>()?.RequestAsync(NeteaseApis.PlaylistTracksEditApi,` | Direct endpoint operation |
| `HyPlayer\UI\Lists\PlaylistItem.xaml.cs` | 8 | `using HyPlayer.NeteaseApi;` | Coupling/import/handler/session/serialization reference |
| `HyPlayer\UI\Lists\PlaylistItem.xaml.cs` | 9 | `using HyPlayer.NeteaseApi.ApiContracts;` | Coupling/import/handler/session/serialization reference |
| `HyPlayer\UI\Lists\PlaylistItem.xaml.cs` | 10 | `using HyPlayer.NeteaseApi.ApiContracts.Playlist;` | Coupling/import/handler/session/serialization reference |
| `HyPlayer\UI\Lists\PlaylistItem.xaml.cs` | 27 | `private readonly NeteaseCloudMusicApiHandler _api = Ioc.Default.GetRequiredService<NeteaseCloudMusicApiHandler>();` | Coupling/import/handler/session/serialization reference |
| `HyPlayer\UI\Lists\PlaylistItem.xaml.cs` | 67 | `var result = await _api.RequestAsync(NeteaseApis.PlaylistPrivacyApi,` | Direct endpoint operation |
| `HyPlayer\UI\Lists\PlaylistItem.xaml.cs` | 85 | `var result = await _api.RequestAsync(NeteaseApis.PlaylistDeleteApi,` | Direct endpoint operation |
| `HyPlayer\UI\Lists\SongsList.xaml.cs` | 13 | `using HyPlayer.NeteaseApi;` | Coupling/import/handler/session/serialization reference |
| `HyPlayer\UI\Lists\SongsList.xaml.cs` | 14 | `using HyPlayer.NeteaseApi.ApiContracts;` | Coupling/import/handler/session/serialization reference |
| `HyPlayer\UI\Lists\SongsList.xaml.cs` | 15 | `using HyPlayer.NeteaseApi.ApiContracts.Cloud;` | Coupling/import/handler/session/serialization reference |
| `HyPlayer\UI\Lists\SongsList.xaml.cs` | 16 | `using HyPlayer.NeteaseApi.ApiContracts.Playlist;` | Coupling/import/handler/session/serialization reference |
| `HyPlayer\UI\Lists\SongsList.xaml.cs` | 48 | `private readonly NeteaseCloudMusicApiHandler _api = Ioc.Default.GetRequiredService<NeteaseCloudMusicApiHandler>();` | Coupling/import/handler/session/serialization reference |
| `HyPlayer\UI\Lists\SongsList.xaml.cs` | 377 | `await _api.RequestAsync(NeteaseApis.PlaylistTracksEditApi,` | Direct endpoint operation |
| `HyPlayer\UI\Lists\SongsList.xaml.cs` | 387 | `await _api.RequestAsync(NeteaseApis.CloudDeleteApi,` | Direct endpoint operation |
| `HyPlayer\UI\Playback\PlayBar\PlayBar.xaml.cs` | 12 | `using HyPlayer.NeteaseApi;` | Coupling/import/handler/session/serialization reference |
| `HyPlayer\UI\Playback\PlayBar\PlayBar.xaml.cs` | 13 | `using HyPlayer.NeteaseApi.ApiContracts;` | Coupling/import/handler/session/serialization reference |
| `HyPlayer\UI\Playback\PlayBar\PlayBar.xaml.cs` | 14 | `using HyPlayer.NeteaseApi.ApiContracts.PersonalFM;` | Coupling/import/handler/session/serialization reference |
| `HyPlayer\UI\Playback\PlayBar\PlayBar.xaml.cs` | 62 | `private readonly NeteaseCloudMusicApiHandler _api = Ioc.Default.GetRequiredService<NeteaseCloudMusicApiHandler>();` | Coupling/import/handler/session/serialization reference |
| `HyPlayer\UI\Playback\PlayBar\PlayBar.xaml.cs` | 437 | `_taskRunner.Forget(_api.RequestAsync(NeteaseApis.PersonalFmTrashApi,` | Direct endpoint operation |

## Operation Categories Covered

- Add/Remove playlist item: 2
- Comment operations: 3
- Create/Delete/Privacy playlist: 5
- Daily Songs: 2
- Like/Unlike: 3
- Listen Together: 8
- Login/Logout/Session: 6
- Lyrics: 2
- Personal FM: 2
- Player trash/feedback: 1
- Recommendations: 5
- Search: 11
- Song/Album/Artist/Playlist details: 23
- Stream URL: 5
- User Library: 18

Required categories status: Stream URL, Lyrics, Song/Album/Artist/Playlist details, Search, Recommendations, Personal FM, Like/Unlike, Add/Remove playlist item, Create/Delete/Privacy playlist, Comment operations, Login/Logout/Session, User Library, Daily Songs, and Player trash/feedback are present. Listen Together and video/mlog/radio/cloud upload operations were also found and marked where contracts are missing.

## Operations Lacking Existing Contracts

| Category | Endpoint/API | Needed contract description |
|---|---|---|
| Comment operations | `CommentFloorApi` | NEEDS NEW CONTRACT - threaded/floor comments provider |
| Comment operations | `CommentLikeApi` | NEEDS NEW CONTRACT - comment like/unlike |
| Comment operations | `CommentsApi` | NEEDS NEW CONTRACT - comments query provider |
| Create/Delete/Privacy playlist | `PlaylistCreateApi` | NEEDS NEW CONTRACT - playlist management create |
| Create/Delete/Privacy playlist | `PlaylistDeleteApi` | NEEDS NEW CONTRACT - playlist management delete |
| Create/Delete/Privacy playlist | `PlaylistPrivacyApi` | NEEDS NEW CONTRACT - playlist privacy management |
| Listen Together | `ListenTogetherPlayCommandApi` | NEEDS NEW CONTRACT - listen-together playback command sync |
| Listen Together | `ListenTogetherRoomCheckApi` | NEEDS NEW CONTRACT - listen-together join/check |
| Listen Together | `ListenTogetherRoomCreateApi` | NEEDS NEW CONTRACT - listen-together room lifecycle |
| Listen Together | `ListenTogetherStatusApi` | NEEDS NEW CONTRACT - listen-together heartbeat/status |
| Listen Together | `ListenTogetherSyncListReportApi` | NEEDS NEW CONTRACT - listen-together queue sync |
| Login/Logout/Session | `LoginAnnounceDeviceApi` | NEEDS NEW CONTRACT - provider-owned device announce |
| Login/Logout/Session | `LoginCellphoneApi` | NEEDS NEW CONTRACT - provider-owned NetEase auth/session |
| Login/Logout/Session | `LoginEmailApi` | NEEDS NEW CONTRACT - provider-owned NetEase auth/session |
| Login/Logout/Session | `LoginQrCodeCheckApi` | NEEDS NEW CONTRACT - provider-owned QR login polling |
| Login/Logout/Session | `LoginQrCodeUnikeyApi` | NEEDS NEW CONTRACT - provider-owned QR login session |
| Login/Logout/Session | `LoginStatusApi` | NEEDS NEW CONTRACT - provider-owned login status/session restore |
| Personal FM | `AiDjContentRcmdInfoApi` | NEEDS NEW CONTRACT - AI DJ / Personal FM metadata stream |
| Personal FM | `PersonalFmApi` | NEEDS NEW CONTRACT - IRecommendationProvidable or  stateful Personal FM stream |
| Player trash/feedback | `PersonalFmTrashApi` | NEEDS NEW CONTRACT - provider playback feedback/trash |
| Recommendations | `MlogRcmdFeedListApi` | NEEDS NEW CONTRACT - video/mlog feed recommendation |
| Recommendations | `PlaylistCategoryListApi` | NEEDS NEW CONTRACT - playlist category catalog |
| Recommendations | `PlaymodeIntelligenceListApi` | NEEDS NEW CONTRACT - intelligence playmode recommendation |
| Search | `SearchSuggestionApi` | NEEDS NEW CONTRACT - search suggestion provider |
| Song/Album/Artist/Playlist details | `AlbumDetailDynamicApi` | NEEDS NEW CONTRACT - IProvidableItemProvidable plus NEEDS NEW CONTRACT for dynamic metadata |
| Song/Album/Artist/Playlist details | `ArtistAlbumsApi` | NEEDS NEW CONTRACT - IProvidableItemRangeProvidable or NEEDS NEW CONTRACT for artist albums |
| Song/Album/Artist/Playlist details | `ArtistSongsApi` | NEEDS NEW CONTRACT - IProvidableItemRangeProvidable or NEEDS NEW CONTRACT for paged artist songs |
| Song/Album/Artist/Playlist details | `DjChannelProgramsApi` | NEEDS NEW CONTRACT - IProvidableItemRangeProvidable or NEEDS NEW CONTRACT for radio programs |
| Song/Album/Artist/Playlist details | `MlogDetailApi` | NEEDS NEW CONTRACT - mlog detail provider |
| Song/Album/Artist/Playlist details | `VideoDetailApi` | NEEDS NEW CONTRACT - video detail provider |
| Stream URL | `MlogUrlApi` | NEEDS NEW CONTRACT - mlog media resource provider |
| Stream URL | `VideoUrlApi` | NEEDS NEW CONTRACT - video media resource provider |
| User Library | `AlbumSublistApi` | NEEDS NEW CONTRACT - NEEDS NEW CONTRACT or IProvableItemLikable.GetLikedProvidableIdsAsync for album library |
| User Library | `ArtistSublistApi` | NEEDS NEW CONTRACT - NEEDS NEW CONTRACT or IProvableItemLikable.GetLikedProvidableIdsAsync for artist library |
| User Library | `CloudDeleteApi` | NEEDS NEW CONTRACT - cloud library delete |
| User Library | `CloudGetApi` | NEEDS NEW CONTRACT - user library/cloud operation |
| User Library | `CloudPubApi` | NEEDS NEW CONTRACT - cloud upload publish |
| User Library | `CloudUploadCheckApi` | NEEDS NEW CONTRACT - cloud upload workflow |
| User Library | `CloudUploadCoverTokenAllocApi` | NEEDS NEW CONTRACT - cloud upload cover workflow |
| User Library | `CloudUploadInfoApi` | NEEDS NEW CONTRACT - cloud upload metadata commit |
| User Library | `CloudUploadTokenAllocApi` | NEEDS NEW CONTRACT - cloud upload workflow |
| User Library | `DjChannelSubscribedApi` | NEEDS NEW CONTRACT - NEEDS NEW CONTRACT or IProvableItemLikable.GetLikedProvidableIdsAsync for radio library |
| User Library | `NeteaseUploadLoadBalancerGetApi` | NEEDS NEW CONTRACT - cloud upload host discovery |
| User Library | `UserDetailApi` | NEEDS NEW CONTRACT - user library/cloud operation |
| User Library | `UserPlaylistApi` | NEEDS NEW CONTRACT - user library playlist enumeration |
| User Library | `UserRecordApi` | NEEDS NEW CONTRACT - user listening history |

## Playback-Management Migration Inventory

| Area/File | Current dependency | Migration target / note | Risk |
|---|---|---|:---:|
| `HyPlayer/App.xaml.cs` | `AudioGraphPlayer`, `PlaybackStateService`, `IMediaSourceService`, `IPlaybackControlService`, `IPlaylistService`, queue providers, strategies, transitions | Move DI registration to native Depository; keep UWP audio surface app-side unless PlayCore supplies a UWP audio service; wire provider-neutral PlayCore services where possible. | H |
| `Services/Playback/PlaybackStateService.cs` | Playback state, now playing, lyrics, cover, progress notifications | Map to PlayCore notification/state model (`CurrentSongChangedNotification`, `PlaybackPositionChangedNotification`, etc.) while preserving UWP binding surface during transition. | H |
| `Services/Playback/PlaylistService/*` | `IPlaylistService` queue, strategies, source providers, Netease conversion helpers | Replace Netease-specific queue loading with provider abstractions; map queue manager behavior to `PlayListManagerBase` where feasible. | H |
| `Services/Playback/PlaybackControlService.cs` | `IPlaybackControlService`, `AudioGraphPlayer`, `MediaSourceService`, `ILyricService`, playlist/state orchestration | Candidate for `PlayControllerBase`; provider resource resolution should flow through `IMusicResourceProvidable`/provider dispatcher. | H |
| `Services/Playback/MediaProviders/MediaSourceService.cs` | `HyPlayItem.ProviderId` routing to `IMediaSourceProvider` ids `ncm`, `lcl`, `nca`, `nst` | Provider-neutral resource selection should route through PlayCore provider/resource abstractions; UWP `MediaSource` materialization may remain app-side. | H |
| `Services/Playback/LyricService.cs` | Direct `LyricApi`, cache, local/remote lyric parsing, `PlaybackStateService` updates | Use `ILyricProvidable.GetLyricInfoAsync` for provider lyrics; keep local lyric fallback/parser app-side or make provider-neutral service. | M |
| `Services/Playback/MediaProviders/NeteaseStreamingProvider.cs` / `CachedNeteaseProvider.cs` | Direct `SongUrlApi` stream/cached media lookup | Use `IMusicResourceProvidable.GetMusicResourceAsync` from NeteaseProvider; preserve cache policy separately. | M |
| `Services/Playback/QueueProviders/*.cs` | Direct song/album/playlist/radio/artist endpoint loading | Replace with `IProvidableItemProvidable`/`IProvidableItemRangeProvidable`; add contracts for paged radio/program/artist/playlist content if current interfaces are insufficient. | M |
| `Services/Playback/Strategies/PersonalFmStrategy.cs` | `PersonalFmApi`, `AiDjContentRcmdInfoApi` stateful load-more behavior | Needs Personal FM contract beyond generic recommendations or a typed `IRecommendationProvidable` convention. | M |
| `Infrastructure/Netease/ListenTogetherManager.cs` and `ListenTogetherStrategy` | Listen-together room lifecycle and playback command synchronization | NEEDS NEW CONTRACT if feature remains; also crosses playback control/queue synchronization boundary. | H |

### Playback Grep Files

| File | Matched dependencies |
|---|---|
| `HyPlayer\App.xaml.cs` | `AudioGraphPlayer, PlaybackStateService, IPlaylistService, PlaylistService, IPlaybackControlService, MediaSourceService, LyricService, IQueueSourceProvider, QueueSourceProvider, IPlayStrategy, ITrackTransition` |
| `HyPlayer\Domain\Music\SongListQueueScope.cs` | `IQueueSourceProvider, QueueSourceProvider` |
| `HyPlayer\Domain\Settings\PlaybackSettings.cs` | `AudioGraphPlayer, PlaybackStateService, IPlaylistService, PlaylistService, IPlaybackControlService` |
| `HyPlayer\Features\Album\AlbumPageViewModel.cs` | `IPlaylistService, PlaylistService` |
| `HyPlayer\Features\Home\HomeViewModel.cs` | `IPlaylistService, PlaylistService` |
| `HyPlayer\Features\Library\LocalMusicPage.xaml.cs` | `IPlaylistService, PlaylistService` |
| `HyPlayer\Features\Library\MusicCloudPage.xaml.cs` | `IPlaylistService, PlaylistService` |
| `HyPlayer\Features\Playlist\SongListViewModel.cs` | `IPlaylistService, PlaylistService` |
| `HyPlayer\Features\Radio\RadioPage.xaml.cs` | `IPlaylistService, PlaylistService` |
| `HyPlayer\Features\Video\MVPage.xaml.cs` | `IPlaybackControlService` |
| `HyPlayer\Features\Widgets\WidgetPage.xaml.cs` | `AudioGraphPlayer, PlaybackStateService, IPlaylistService, PlaylistService, IPlaybackControlService, LyricService` |
| `HyPlayer\Infrastructure\Netease\Api.cs` | `PlaybackStateService, IPlaylistService, PlaylistService` |
| `HyPlayer\Infrastructure\Netease\ListenTogetherManager.cs` | `PlaybackStateService, IPlaylistService, PlaylistService, IPlaybackControlService` |
| `HyPlayer\Infrastructure\Netease\PersonalFM.cs` | `PlaybackStateService, IPlaylistService, PlaylistService, IPlayStrategy, IAsyncPlayStrategy` |
| `HyPlayer\Services\Abstractions\ILyricService.cs` | `LyricService` |
| `HyPlayer\Services\Abstractions\IMediaSourceProvider.cs` | `MediaSourceService` |
| `HyPlayer\Services\Abstractions\IPlaybackControlService.cs` | `IPlaybackControlService` |
| `HyPlayer\Services\Abstractions\IPlaylistService.cs` | `IPlaylistService, PlaylistService, IPlayStrategy, ITrackTransition` |
| `HyPlayer\Services\Abstractions\IPlayStrategy.cs` | `PlaylistService, IPlayStrategy, IAsyncPlayStrategy` |
| `HyPlayer\Services\Abstractions\IQueueSourceProvider.cs` | `IQueueSourceProvider, QueueSourceProvider` |
| `HyPlayer\Services\Abstractions\ITrackTransition.cs` | `PlaylistService, ITrackTransition` |
| `HyPlayer\Services\Abstractions\NeteaseQueueSourceLoadResult.cs` | `IPlaylistService, PlaylistService, IQueueSourceProvider, QueueSourceProvider` |
| `HyPlayer\Services\Authentication\AuthService.cs` | `PlaybackStateService` |
| `HyPlayer\Services\Navigation\AppNavigator.cs` | `IPlaylistService, PlaylistService` |
| `HyPlayer\Services\Playback\LyricService.cs` | `PlaybackStateService, LyricService` |
| `HyPlayer\Services\Playback\MediaProviders\MediaSourceService.cs` | `MediaSourceService` |
| `HyPlayer\Services\Playback\PlaybackControlService.cs` | `AudioGraphPlayer, PlaybackStateService, IPlaylistService, PlaylistService, IPlaybackControlService, MediaSourceService, LyricService` |
| `HyPlayer\Services\Playback\PlaybackNotificationService.cs` | `AudioGraphPlayer, PlaybackStateService` |
| `HyPlayer\Services\Playback\PlaybackStateService.cs` | `PlaybackStateService` |
| `HyPlayer\Services\Playback\PlaybackSurfaceCoordinator.cs` | `AudioGraphPlayer, PlaybackStateService` |
| `HyPlayer\Services\Playback\PlaybackSurfaceStore.cs` | `PlaybackStateService` |
| `HyPlayer\Services\Playback\PlaylistService\PlaylistService.cs` | `PlaybackStateService, IPlaylistService, PlaylistService, IPlaybackControlService, IQueueSourceProvider, QueueSourceProvider, IPlayStrategy, ITrackTransition` |
| `HyPlayer\Services\Playback\PlaylistService\PlaylistService.Internal.cs` | `PlaybackStateService, PlaylistService` |
| `HyPlayer\Services\Playback\PlaylistService\PlaylistService.LocalFiles.cs` | `PlaylistService` |
| `HyPlayer\Services\Playback\PlaylistService\PlaylistService.Navigation.cs` | `PlaylistService, IAsyncPlayStrategy` |
| `HyPlayer\Services\Playback\PlaylistService\PlaylistService.Netease.cs` | `PlaylistService, QueueSourceProvider` |
| `HyPlayer\Services\Playback\PlaylistService\PlaylistService.ShuffleAndLocal.cs` | `PlaylistService` |
| `HyPlayer\Services\Playback\PlaylistService\PlaylistService.Strategies.cs` | `PlaylistService` |
| `HyPlayer\Services\Playback\PlaylistService\PlaylistService.TrackEnd.cs` | `PlaylistService, IAsyncPlayStrategy` |
| `HyPlayer\Services\Playback\QueueProviders\AlbumQueueSourceProvider.cs` | `IQueueSourceProvider, QueueSourceProvider` |
| `HyPlayer\Services\Playback\QueueProviders\PlaylistQueueSourceProvider.cs` | `IQueueSourceProvider, QueueSourceProvider` |
| `HyPlayer\Services\Playback\QueueProviders\RadioQueueSourceProvider.cs` | `IQueueSourceProvider, QueueSourceProvider` |
| `HyPlayer\Services\Playback\QueueProviders\SingerHotQueueSourceProvider.cs` | `IQueueSourceProvider, QueueSourceProvider` |
| `HyPlayer\Services\Playback\QueueProviders\SingleSongQueueSourceProvider.cs` | `IQueueSourceProvider, QueueSourceProvider` |
| `HyPlayer\Services\Playback\SongListQueueBuilder.cs` | `PlaybackStateService, IPlaylistService, PlaylistService, IQueueSourceProvider, QueueSourceProvider` |
| `HyPlayer\Services\Playback\Strategies\ListenTogetherStrategy.cs` | `IPlayStrategy` |
| `HyPlayer\Services\Playback\Strategies\PersonalFmStrategy.cs` | `PlaylistService, IAsyncPlayStrategy` |
| `HyPlayer\Services\Playback\Strategies\SequentialStrategy.cs` | `IPlayStrategy` |
| `HyPlayer\Services\Playback\Strategies\ShuffleNoRepeatStrategy.cs` | `IPlayStrategy` |
| `HyPlayer\Services\Playback\Strategies\SingleRepeatStrategy.cs` | `IPlayStrategy` |
| `HyPlayer\Services\Playback\Transitions\CrossFadeTransition.cs` | `AudioGraphPlayer, ITrackTransition` |
| `HyPlayer\Services\Playback\Transitions\DirectTransition.cs` | `ITrackTransition` |
| `HyPlayer\Services\Playback\Transitions\GaplessTransition.cs` | `ITrackTransition` |
| `HyPlayer\Shell\BasePage.xaml.cs` | `AudioGraphPlayer, IPlaybackControlService` |
| `HyPlayer\Shell\CompactPlayerPage.xaml.cs` | `AudioGraphPlayer, PlaybackStateService, IPlaylistService, PlaylistService, IPlaybackControlService, LyricService` |
| `HyPlayer\Shell\ExpandedPlayer\ExpandedCanvas\SpectrumLayer.cs` | `AudioGraphPlayer` |
| `HyPlayer\Shell\ExpandedPlayer\ExpandedPlayer.xaml.cs` | `AudioGraphPlayer, PlaybackStateService, IPlaylistService, PlaylistService, IPlaybackControlService, LyricService` |
| `HyPlayer\Shell\ExpandedPlayer\ExpandedPlayerViewModel.cs` | `PlaybackStateService, IPlaylistService, PlaylistService, IPlaybackControlService, LyricService` |
| `HyPlayer\Shell\Playback\ExpandedPlayerShareSaveController.cs` | `PlaybackStateService, IPlaylistService, PlaylistService` |
| `HyPlayer\Shell\TestPage.xaml.cs` | `PlaybackStateService, IPlaylistService, PlaylistService` |
| `HyPlayer\UI\Lists\GroupedSongsList.xaml.cs` | `PlaybackStateService` |
| `HyPlayer\UI\Lists\GroupedSongsListViewModel.cs` | `PlaybackStateService, IPlaylistService, PlaylistService` |
| `HyPlayer\UI\Lists\PlaylistItem.xaml.cs` | `IPlaylistService, PlaylistService` |
| `HyPlayer\UI\Lists\SongsList.xaml.cs` | `PlaybackStateService, IPlaylistService, PlaylistService` |
| `HyPlayer\UI\Playback\LyricControl\LyricControl.xaml.cs` | `AudioGraphPlayer, LyricService` |
| `HyPlayer\UI\Playback\PlayBar\PlayBar.xaml.cs` | `AudioGraphPlayer, PlaybackStateService, IPlaylistService, PlaylistService, IPlaybackControlService` |
| `HyPlayer\UI\Playback\PlayBar\PlayBarViewModel.cs` | `PlaybackStateService, IPlaylistService, PlaylistService, IPlaybackControlService, LyricService` |

## Notes And Risks

- `HyPlayer.NeteaseProvider/NeteaseProvider.cs` already contains adapter implementations for core provider-neutral contracts, so migration should prefer using those rather than adding another app-side NetEase wrapper.
- `HyPlayer/Classes/JsonDefaultContext.cs`, `Infrastructure/Serialization/JsonDefaults.cs`, `Domain/Comments/*`, and settings/session files are not endpoint operations, but they retain direct NetEaseApi type coupling that must be removed or isolated during migration.
- Some existing interfaces are semantically broad enough but may be too shape-poor for paged containers, playlist category lists, search suggestions, comments, cloud upload, and listen-together. Those are marked `NEEDS NEW CONTRACT` rather than guessed.
- `UNCERTAIN` means the endpoint was not observed in the explicit mapping table and no clear abstraction was identified from the files read.
