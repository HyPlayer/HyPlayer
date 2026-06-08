## Learnings

(Initialized)

### Task 3: ContainersContainer Verification

- `ContainersContainer.GetSubContainerAsync` returns `Task<List<ContainerBase>>` - correct for NeteaseUser usage
- `IProgressiveLoadingContainer` is an **interface** (not a base class) in `Interfaces/PlayListContainer/`
- Progressive loading uses pattern: `LinerContainerBase` + `IProgressiveLoadingContainer` (multiple inheritance via interface)
- `DefaultPlayListManager` consumes progressive containers via `container is IProgressiveLoadingContainer` check
- No `ProgressiveLoadingContainer` base class needed - the interface approach is already in place
- `PersonBase` → `ContainersContainer` → `ContainerBase` → `ProvidableItemBase` inheritance chain is clean

### Task 4: Legacy Reference Script (2026-05-27)

Created `scripts/find-legacy-references.ps1` to find all legacy model references.

**Key findings:**
- Total legacy references found: **455**
- NCArtist has the most references: 121
- NCPlayList: 67 references
- NCUser: 56 references
- using HyPlayer.Domain.Music (blanket import): 54 references
- HyPlayItemType: 51 references
- NCAlbum: 42 references
- Infrastructure.Netease: 31 references
- NCMlog: 19 references
- NCRadio: 8 references
- NCMFile: 6 references

**Files with most legacy references:**
- HyPlayer/UI/Lists/SongListItemViewModel.cs - Heavy usage of NC* types
- HyPlayer/Infrastructure/Netease/Mapper.cs - Maps between legacy and new types
- HyPlayer/Services/Authentication/AuthService.cs - Uses NCUser, NCPlayList
- HyPlayer/Services/Navigation/AppNavigator.cs - Uses NCPlayList, NCUser

**Script features:**
- Searches for using HyPlayer.Domain.Music (excluding allowed types: SimpleListItem, SongListQueueScope, MusicResource)
- Searches for all NC* model types: NCPlayList, NCArtist, NCAlbum, NCUser, NCRadio, NCMlog, NCMFile
- Searches for HyPlayItemType enum
- Searches for Infrastructure.Netease references
- Outputs summary, detailed results by file, and flat format
