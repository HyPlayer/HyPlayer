<h1 align="center">
  <br>
  <img src="https://raw.githubusercontent.com/kengwang/HyPlayer/master/HyPlayer/Assets/icon.png" width="150"/>
  <br>
  HyPlayer
  <br>
</h1>
<h4 align="center">第三方网易云音乐播放器</h4>
<h4 align="center">A Third-party Netease Cloud Music Player</h4>
<p align="center">
	<img alt="Using GPL-v3" src="https://img.shields.io/github/license/kengwang/HyPlayer">
	<img alt="GitHub release (latest by date)" src="https://img.shields.io/github/v/release/kengwang/HyPlayer">
    <img alt="GitHub issues" src="https://img.shields.io/github/issues/HyPlayer/HyPlayer">
    <h4 align="center">本软件仅供学习交流使用  请勿用于其他用途<br /><br />下载后请在 24 小时内删除
</h4>
</p>



## 反馈 & 交流

用户交流 QQ 群: <a href="https://jq.qq.com/?_wv=1027&k=cQ73ZhqY">1145646224</a>

> 建议首选 QQ 群组, 下列方式仅为紧急情况使用

Telegram 群组: https://t.me/joinchat/6tJqI3m-b402NDRl

Telegram 频道: https://t.me/hyplayer

## 声明

本软件非盈利性软件,且遵循 [**GPL-v3**](LICENCE) 协议,请勿将此软件用于商业用途.

本软件**不提供** VIP 音源破解等服务, 你需要在对应平台取得相应身份才能进行播放

所有内容资源 (包括但不限于音源, 图片等) 版权归网易云音乐所有

本软件仅学习交流使用. 如有侵权,请发 Issue 提出.

## 下载

目前已经在 Microsoft Store 下架，请使用 AppCenter 或 GitHub 通道进行下载

注意：在第一次安装时请额外下载基础包，后续更新仅需下载版本包即可

|分发方式|分发链接|分发状态|
|-------|-------|-------|
| App Center Release (**推荐**) | [申请链接](https://hyplayer.kengwang.com.cn/#/insider) | ![Build Status](https://dev.azure.com/kengwang/HyPlayer/_apis/build/status/AppCenter%20-%20Release?branchName=develop) |
| App Center Canary | [申请链接](https://hyplayer.kengwang.com.cn/#/insider) | [![Build Status](https://dev.azure.com/kengwang/HyPlayer/_apis/build/status/AppCenter%20-%20Canary?branchName=develop)](https://dev.azure.com/kengwang/HyPlayer/_build/latest?definitionId=34&branchName=develop) |
| Microsoft Store | [商店链接](https://www.microsoft.com/store/productId/9N5TD916686K) | **已废弃** |
| Azure DevOps | [分发链接](https://dev.azure.com/kengwang/HyPlayer/_build) | [![Build Status](https://dev.azure.com/kengwang/HyPlayer/_apis/build/status/Github%20Nightly?branchName=develop)](https://dev.azure.com/kengwang/HyPlayer/_build/latest?definitionId=33&branchName=develop) |
| Github Release | [分发链接](https://github.com/HyPlayer/HyPlayer/releases/latest) | ![Release Status](https://img.shields.io/github/v/release/kengwang/HyPlayer) |
| 基础包 (App Center) | [分发链接](https://install.appcenter.ms/users/kengwang/apps/hyplayer/distribution_groups/base%20packages) | 常驻 |



## 界面预览

[界面预览](PREVIEW.md)

## 隐私策略

使用此应用即代表您同意 [网易云音乐隐私策略](https://st.music.163.com/official-terms/privacy#) 以及 [HyPlayer 隐私策略](PrivacyPolicy.md)

## 相关说明

### 软件性质

因为作者忙于学业,通常只会在周末处理相关事情

请勿将此软件用于 UWP 入门学习, 由于作者没利用好 MVVM 导致项目混乱.

请勿模仿

### 关于桌面歌词

本软件内置了以Toast通知形式实现的桌面歌词，如果有拖动桌面歌词/控制歌曲的需要我们建议下载[热词app](https://apps.microsoft.com/store/detail/9MXFFHVQVBV9)

### 关于无版权

HyPlayer 不内置解灰以及 VIP 歌曲解锁, 且不会在之后的版本中内置解灰

你可以通过使用 [UnblockNeteaseMusic](https://github.com/UnblockNeteaseMusic/server) 进行解灰.

解灰教程已在[Wiki](https://github.com/HyPlayer/HyPlayer/wiki/%E5%85%B3%E4%BA%8E%E4%BD%BF%E7%94%A8-UnblockNeteaseMusic-%E7%9A%84%E6%96%B9%E6%B3%95)中列出

将其设置为系统代理并在 `设置` - `实验室` 中勾选降级为 HTTP 并在代理服务器中填入你的代理

在使用代理服务器前,你可能需要解除 UWP 网络环回限制

```powershell
CheckNetIsolation LoopbackExempt -a -n="48848aaaaaaccd.hyplayer_fkcggvf9kbkw0"
```

## 使用

* NeteaseCloudMusicApi
  [wwh1004/NeteaseCloudMusicApi](https://github.com/wwh1004/NeteaseCloudMusicApi) => [HyPlayer/NeteaseCloudMusicApi](https://github.com/HyPlayer/NeteaseCloudMusicApi) (MIT Licence)
* Kawazu [Cutano/Kawazu](https://github.com/Cutano/Kawazu) => [HyPlayer / Kawazu](https://github.com/HyPlayer/Kawazu) (MIT Licence)
* Windows UI Library [microsoft/microsoft-ui-xaml](https://github.com/microsoft/microsoft-ui-xaml) (MIT Licence)
* Windows Community Toolkit [CommunityToolkit/WindowsCommunityToolkit](https://github.com/CommunityToolkit/WindowsCommunityToolkit) (MIT Licence)
* TagLibSharp [mono/taglib-sharp](https://github.com/mono/taglib-sharp) (LGPL)
* Opportunity.LrcParser [OpportunityLiu/LrcParser](https://github.com/OpportunityLiu/LrcParser) ([Apache-2.0 Licence](https://github.com/OpportunityLiu/LrcParser/blob/master/LICENSE))
* Inflatable Last.fm [inflatablefriends/lastfm](https://github.com/inflatablefriends/lastfm) ([MIT Licence](https://github.com/inflatablefriends/lastfm/blob/next/LICENCE.md))



> 如有许可协议使用不当请发 Issue 或者 Pull Request
>
> If any of the licenses are not being used correctly, please submit a new issue.

## 代码参考

* NLyric [wwh1004/NLyric](https://github.com/wwh1004/NLyric)
* ncmdump [anonymous5l/ncmdump-gui](https://github.com/anonymous5l/ncmdump-gui)

## 开发者

* 主开发者: [Kengwang](https://github.com/kengwang)
* UI 设计: [aaaaaaccd](https://github.com/aaaaaaccd)
* 部分功能: [EP012014 (天湖)](https://github.com/EP012014)
* 部分功能: [Raspberry Kan](https://github.com/Raspberry-Monster)
* 部分功能和一些修复: [Claris](https://github.com/ClarisS01017)
* 图标提供 / UI 设计: [FUNNYTW](https://www.coolapk.com/u/1873068)
* UI 设计: [Betta_Fish](https://github.com/zxbmmmmmmmmm)
* [Contributors](https://github.com/HyPlayer/HyPlayer/graphs/contributors)

## 捐助

爱发电: https://afdian.net/@kengwang

以太坊：0xad13286a373221672a4b4fdb6607eb2d2aa5966a

感谢您的支持!

## 感谢

<img src="https://www.jetbrains.com/shop/static/images/jetbrains-logo-inv.svg" height="100">

感谢由 [Jetbrains](https://www.jetbrains.com) 提供的 [开源许可证书](https://www.jetbrains.com/community/opensource/) 

此项目部分内容通过 [Rider](https://www.jetbrains.com/rider/) 进行开发.
