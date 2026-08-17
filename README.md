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
| Release | [申请链接](https://hyplayer.kengwang.com.cn/#/insider) | ![Build Status](https://dev.azure.com/kengwang/HyPlayer/_apis/build/status/AppCenter%20-%20Release?branchName=develop) |
| Canary | [申请链接](https://hyplayer.kengwang.com.cn/#/insider) | [![Build Status](https://dev.azure.com/kengwang/HyPlayer/_apis/build/status/AppCenter%20-%20Canary?branchName=develop)](https://dev.azure.com/kengwang/HyPlayer/_build/latest?definitionId=34&branchName=develop) |
| Microsoft Store | [商店链接](https://www.microsoft.com/store/productId/9N5TD916686K) | **已废弃** |
| GitHub Actions | [分发链接](https://github.com/HyPlayer/HyPlayer/releases/tag/actions-build) | [![.NET Core Desktop](https://github.com/HyPlayer/HyPlayer/actions/workflows/dotnet-desktop.yml/badge.svg?branch=develop)](https://github.com/HyPlayer/HyPlayer/actions/workflows/dotnet-desktop.yml) |
| Github Release | [分发链接](https://github.com/HyPlayer/HyPlayer/releases/latest) | ![Release Status](https://img.shields.io/github/v/release/kengwang/HyPlayer) |



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

## 依赖

### 源码及项目引用

| 项目 | 用途及说明 | 许可证 |
| --- | --- | --- |
| [HyPlayer.NeteaseProvider](https://github.com/HyPlayer/HyPlayer.NeteaseProvider) | 网易云音乐服务与 API；API 实现源自 [NeteaseCloudMusicApi](https://github.com/wwh1004/NeteaseCloudMusicApi) | MIT |
| [HyPlayer.PlayCore](https://github.com/HyPlayer/HyPlayer.PlayCore) | 播放核心及抽象 | MIT |
| [HyPlayer.Frieren](https://github.com/HyPlayer/HyPlayer.Frieren) | UWP 控件、通知及辅助代码，部分代码源自 Windows Community Toolkit | MIT |
| `HyPlayer.UWP.Chopin`、`HyPlayer.LyricEffects` | 本仓库中的播放实现与歌词特效项目 | GPL-3.0（本仓库） |
| [Kawazu](https://github.com/HyPlayer/Kawazu)（源自 [Cutano/Kawazu](https://github.com/Cutano/Kawazu)） | 日文分词 | MIT |
| [Impressionist](https://github.com/Storyteller-Studios/Impressionist) | 图像取色与量化 | MIT |
| [ObservableCollections](https://github.com/Cysharp/ObservableCollections) | 高性能可观察集合；为适配 UWP XAML/CsWinRT，源码被单独提取到 [`ObservableCollections/`](ObservableCollections/)，加入了基于 `ObservableList<T>` 的 CommunityToolkit 增量加载实现，并在 `CoreCompile` 前生成 WinRT 闭合泛型暴露信息，**没有直接引用上游 NuGet 包或原程序集**。详见 [兼容性与修改说明](ObservableCollections-CsWinRT-Fix.md) | MIT |
| `Microsoft.Gaming.XboxGameBar.Projection` | 本仓库中的 Game Bar SDK CsWinRT 投影项目 | 本仓库代码为 GPL-3.0；SDK 使用 Microsoft Software License Terms |

### 应用及运行时 NuGet 依赖

| 项目 | 当前直接引用版本 | 许可证 |
| --- | --- | --- |
| [ALRC](https://github.com/kengwang/ALRC)（`ALRC.Abstraction`、`ALRC.Converters`） | 1.3.0 / 1.3.2 | CC0-1.0 |
| [AsyncAwaitBestPractices](https://github.com/brminnick/AsyncAwaitBestPractices) | 10.0.0 | MIT |
| [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet) | 8.4.2 | MIT |
| [Windows Community Toolkit](https://github.com/CommunityToolkit/Windows)（Animations、Behaviors、Collections 源码适配、Controls、Converters、Extensions、Helpers、Media） | 8.2.251219 / `main` 源码适配 | MIT |
| [CommunityToolkit Labs UWP TitleBar](https://github.com/CommunityToolkit/Labs-Windows) | 0.1.251217-build.2433 | MIT |
| [ComputeSharp](https://github.com/Sergio0694/ComputeSharp)（`ComputeSharp.D2D1.Uwp`） | 3.2.0 | MIT |
| [Depository](https://github.com/kengwang/Depository)（含 Abstraction、DependencyInjection 扩展） | 4.0.1 | MIT |
| [Dynamic Expresso](https://github.com/dynamicexpresso/DynamicExpresso) | 2.19.3 | MIT |
| [StringSimilarity.NET](https://github.com/feature23/StringSimilarity.NET) | 7.0.1 | MIT |
| [LibNMeCab](https://github.com/komutan/NMeCab) | 0.10.2 | GPL-2.0-or-later OR LGPL-2.1-or-later |
| [LiteFM](https://github.com/Storyteller-Studios/LiteFM) | 1.0.3 | MIT |
| [.NET](https://github.com/dotnet/dotnet)（`Microsoft.Extensions.DependencyInjection`、`System.Text.Json`） | 10.0.10 | MIT |
| [Windows UI Library](https://github.com/microsoft/microsoft-ui-xaml) | 2.8.7 | Microsoft Software License Terms |
| [XAML Behaviors for UWP](https://github.com/microsoft/XamlBehaviors) | 3.0.1 | MIT |
| [Microsoft Game Bar SDK](https://www.xbox.com/pc-gaming) / [CsWinRT](https://github.com/microsoft/CsWinRT) | 7.3.2607010 / 2.3.1 | Microsoft Software License Terms / MIT |
| [Polly](https://github.com/App-vNext/Polly) | 8.7.0 | BSD-3-Clause |
| [QRCoder](https://github.com/Shane32/QRCoder) | 1.8.0 | MIT |
| [TagLibSharp](https://github.com/mono/taglib-sharp) | 2.3.0 | LGPL-2.1-only |
| [Vanara](https://github.com/dahall/vanara)（`Vanara.Core`） | 5.0.5 | MIT |
| [Vortice.Windows](https://github.com/amerkoleci/Vortice.Windows)（Direct3D11、DirectX） | 3.8.3 | MIT |

### 构建与测试依赖

| 项目 | 当前直接引用版本 | 许可证 |
| --- | --- | --- |
| [Microsoft Windows SDK Build Tools](https://www.nuget.org/packages/Microsoft.Windows.SDK.BuildTools) | 10.0.28000.2526 | Microsoft Windows SDK License |
| [T4.Build](https://github.com/jgiannuzzi/T4.Build) | 0.2.5 | Apache-2.0 |
| [TUnit](https://github.com/thomhurst/TUnit) | 1.62.0 / 1.9.55 | MIT |
| [AwesomeAssertions](https://github.com/AwesomeAssertions/AwesomeAssertions) | 9.3.0 | Apache-2.0 |

> 如有遗漏或许可协议使用不当，请提交 Issue 或 Pull Request。
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

感谢您的支持!

## 感谢

<table>
  <tr>
    <td>
      <img alt="SignPath" src="https://signpath.org/assets/favicon-50x50.png" />
    </td>
    <td>
    Free code signing on Windows provided by <a href="https://signpath.io">SignPath.io</a>, certificate by <a href="https://signpath.org/">SignPath Foundation</a><br/>
    由 <a href="https://signpath.io">SignPath.io</a> 提供 Windows 上的免费代码签名，由 <a href="https://signpath.org">SignPath Foundation</a> 提供证书
    </td>
  </tr>
</table>

<img src="https://www.jetbrains.com/shop/static/images/jetbrains-logo-inv.svg" height="100">

感谢由 [Jetbrains](https://www.jetbrains.com) 提供的 [开源许可证书](https://www.jetbrains.com/community/opensource/) 

此项目部分内容通过 [Rider](https://www.jetbrains.com/rider/) 进行开发.
