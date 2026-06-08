
## Task 9: NavigationShellViewModel Migration (2026-05-27)
- _auth.CurrentUser is NeteaseUser? (from IAuthService), not NCUser
- _auth.MySongLists is List<NeteasePlaylist>, not List<NCPlayList>
- NeteaseUser inherits PersonBase which has: Name, ItemId, ProviderId, TypeId, ActualId
- NeteaseUser has: AvatarUrl, Description, Gender, BackgroundUrl, Followed, VipType
- Property mapping applied: Avatar -> AvatarUrl, Signature -> Description, Id -> ActualId
- NeteaseUserPlaylistSubContainer class doesn't exist in codebase; NeteaseUser.GetSubContainerAsync() returns NeteasePlaylist objects directly
- Simplified loop: containers.OfType<NeteasePlaylist>().ToList() instead of iterating sub-containers
