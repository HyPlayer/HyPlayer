
## Task 10: MeViewModel.cs Migration
- NeteaseUser inherits Name/ActualId from PersonBase, has AvatarUrl/Description directly
- NeteasePlaylist has CoverUrl, Creator (NeteaseUser?), ActualId, Subscribed, PlayCount, TrackCount
- NeteasePlaylist.Creator is nullable (NeteaseUser?) — use ?. for all creator property access
- NCUser.Id maps to NeteaseUser.ActualId, NCUser.Avatar maps to NeteaseUser.AvatarUrl
- NCPlayList.PlaylistId maps to NeteasePlaylist.ActualId, NCPlayList.Cover maps to NeteasePlaylist.CoverUrl
- NCPlayList.HasSubscribed maps to NeteasePlaylist.Subscribed
- HyPlayer.Domain.Music namespace still needed for SimpleListItem and MusicResource even after removing NCUser/NCPlayList refs
- NetEaseUserPlaylistSubContainer type doesn't exist as a class definition — pre-existing issue in codebase
- Build has many pre-existing errors from other files still using old NCUser/NCPlayList types
