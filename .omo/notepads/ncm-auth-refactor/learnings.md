# Learnings - NCM Auth Refactor

## AuthService.cs Refactoring (Task 6)

### Property Mapping Confirmed
- `NCUser.Id` → `NeteaseUser.ActualId` (inherited from `ProvidableItemBase`)
- `NCUser.Avatar` → `NeteaseUser.AvatarUrl`
- `NCUser.Signature` → `NeteaseUser.Description`
- `NCPlayList.PlaylistId` → `NeteasePlaylist.ActualId`
- `NCPlayList.Cover` → `NeteasePlaylist.CoverUrl`
- `NCPlayList.HasSubscribed` → `NeteasePlaylist.Subscribed`

### Key Pattern: Incremental Refactoring
When changing an interface property type, downstream consumers will fail. This is expected behavior for incremental refactoring.

### NetEaseUser/NeteasePlaylist Location
- Both in `HyPlayer.NeteaseProvider.Models` namespace
- `NeteaseUser` extends `PersonBase` → `ProvidableItemBase` (has `ActualId`, `Name`)
- `NeteasePlaylist` extends `LinerContainerBase` → `ContainerBase` → `ProvidableItemBase`

### Build Command Pattern
```
msbuild HyPlayer.slnx /p:Configuration=Release /p:RuntimeIdentifier=win-x64 /p:Platform=x64 /p:AppxBundle=Never /p:AppxPackageSigningEnabled=false /m
```
