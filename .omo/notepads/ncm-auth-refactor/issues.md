# Issues - NCM Auth Refactor

## Downstream Errors After AuthService.cs Refactoring (Task 6)

### Files Using NeteaseUser.Id (should be ActualId)
- Domain\Comments\Comment.cs(22,112)
- Features\Library\HIstoryPage.xaml.cs(118,66)
- Features\Library\HIstoryPage.xaml.cs(123,44)
- Features\User\Me.xaml.cs(46,60)
- Features\Playlist\SongListViewModel.cs(203,114)
- Infrastructure\Netease\ListenTogetherManager.cs(264,85)
- Shell\Navigation\NavigationShellViewModel.cs(190,146)

### Files Using NeteaseUser.Avatar (should be AvatarUrl)
- Shell\Navigation\NavigationShellViewModel.cs(157,61)
- Shell\Navigation\NavigationShellViewModel.cs(159,48)

### Files Using NeteaseUser.Signature (should be Description)
- Shell\Navigation\NavigationShellViewModel.cs(162,57)
- Shell\Navigation\NavigationShellViewModel.cs(162,86)

### Files Using NCPlayList.PlaylistId (should be ActualId)
- MainPage.xaml.cs(176,165)
- Features\Home\HomeViewModel.cs(107,120)
- UI\Dialogs\SongListSelectDialog.xaml.cs(29,115)
- Services\Navigation\AppNavigator.cs(510,79)
- Services\Navigation\AppNavigator.cs(543,87)

### Files Using NCPlayList Type (should be NeteasePlaylist)
- Shell\Navigation\NavigationShellViewModel.cs(212,35)
- Shell\Navigation\NavigationShellViewModel.cs(232,43)

### Files Assigning NeteaseUser to NCUser
- Shell\TestPage.xaml.cs(166,27)
