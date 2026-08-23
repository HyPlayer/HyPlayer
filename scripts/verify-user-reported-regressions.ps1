$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$failures = [System.Collections.Generic.List[string]]::new()

function Assert-SourceContains {
    param(
        [Parameter(Mandatory)] [string] $Path,
        [Parameter(Mandatory)] [string] $Pattern,
        [Parameter(Mandatory)] [string] $Message
    )

    $fullPath = Join-Path $repoRoot $Path
    if (-not (Test-Path -LiteralPath $fullPath)) {
        $failures.Add($Message)
        return
    }

    $content = Get-Content -Raw -LiteralPath $fullPath
    if ($content -notmatch $Pattern) {
        $failures.Add($Message)
    }
}

Assert-SourceContains 'HyPlayer.NeteaseProvider/HyPlayer.NeteaseProvider/NeteaseProvider.cs' `
    'ExtensionName\s*=.*songUrl\.(Type|EncodeType)' `
    '下载资源没有使用歌曲 URL API 返回的文件格式。'
Assert-SourceContains 'HyPlayer/Platform/Downloads/DownloadObject.cs' `
    'NormalizeAudioExtension' `
    '下载文件扩展名没有在使用前规范化。'
Assert-SourceContains 'HyPlayer/Platform/Downloads/DownloadObject.cs' `
    'TagLibHelper\.Create\(streamAbstraction,\s*"\."\s*\+\s*_downloadFormat\)' `
    '写标签时没有继续使用 API 确定的文件格式。'

Assert-SourceContains 'HyPlayer/Domain/UserDisplay.cs' `
    'Uri\?\s+AvatarUri' `
    '评论用户的空头像仍会被强制构造成 Uri。'
Assert-SourceContains 'HyPlayer/Domain/UserDisplay.cs' `
    'Uri\.TryCreate' `
    '评论用户头像 URL 没有安全解析。'
Assert-SourceContains 'HyPlayer/UI/Controls/SingleComment.xaml.cs' `
    'MainCommentProperty[\s\S]*OnMainCommentChanged' `
    '评论控件没有在 MainComment 绑定解除或复用时响应依赖属性变化。'
Assert-SourceContains 'HyPlayer/UI/Controls/SingleComment.xaml.cs' `
    'CommentBase\?\s+MainComment' `
    '评论控件仍错误地把可暂时为空的 MainComment 声明为非空。'
Assert-SourceContains 'HyPlayer/UI/Controls/SingleComment.xaml.cs' `
    'ReferenceEquals\(comment,\s*MainComment\)' `
    '评论头像异步加载没有防止虚拟化复用后写入旧用户信息。'

Assert-SourceContains 'HyPlayer/UI/Lists/ProvidableItemRowViewModel.cs' `
    'HomeCover' `
    '推荐单曲没有独立的主页封面尺寸 URL。'
Assert-SourceContains 'HyPlayer/Features/Home/HomePage.xaml' `
    'UriSource="\{x:Bind HomeCover\}"' `
    '推荐单曲卡片没有绑定主页尺寸封面。'

Assert-SourceContains 'HyPlayer/UI/Lists/ProvidableItemDisplayResolver.cs' `
    '(?m)^\s*Album\s*=\s*item\s+is\s+SingleSongBase\s+\w+\s*\?\s*\w+\.Album' `
    '歌曲行没有保留歌曲所属专辑，菜单无法跳转。'

Assert-SourceContains 'HyPlayer.PlayCore/HyPlayer.PlayCore.Abstraction/Interfaces/ProvidableItem/IHasPublishTime.cs' `
    'long\s+PublishTime' `
    '提供程序抽象缺少专辑发行时间。'
Assert-SourceContains 'HyPlayer.NeteaseProvider/HyPlayer.NeteaseProvider/Mappers/AlbumDataToNeteaseAlbumMapper.cs' `
    'PublishTime\s*=\s*data\.PublishTime' `
    '专辑 API 的发行时间没有映射到提供程序模型。'
Assert-SourceContains 'HyPlayer/Features/Album/AlbumPageViewModel.cs' `
    'IHasPublishTime' `
    '专辑页面没有读取提供程序返回的发行时间。'

Assert-SourceContains 'HyPlayer/Features/Artist/ArtistPage.xaml' `
    'MaxLines="2"' `
    '歌手简介默认没有折叠为两行。'

if ($failures.Count -gt 0) {
    $failures | ForEach-Object { Write-Host "FAIL: $_" }
    exit 1
}

Write-Host 'All user-reported regression checks passed.'
