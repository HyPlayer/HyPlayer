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
</p>
<p align="center">
主开发者: <a href="https://github.com/kengwang">@kengwang</a> | 界面设计: <a href="https://github.com/aaaaaaccd">@aaaaaaccd</a> | 部分功能开发者: <a href="https://github.com/EP012014">@EP012014</a> | <a href="PREVIEW.md">界面预览</a>
</p>


**本软件仅供学习交流使用  请勿用于其他用途**

## 反馈 & 交流

用户交流 QQ 群: <a href="https://jq.qq.com/?_wv=1027&k=cQ73ZhqY">1145646224</a>

Telegram 群组: https://t.me/joinchat/6tJqI3m-b402NDRl

Telegram 频道: https://t.me/hyplayer

Skype 群组: https://join.skype.com/umOViUQNItVd

## 下载

目前已经上架 Microsoft Store,但是更新速度较为缓慢

Microsoft Store: [点击查看](https://www.microsoft.com/store/productId/9N5TD916686K)

[Azure DevOps](https://dev.azure.com/kengwang/HyPlayer/_build/latest?definitionId=29&branchName=master) : [![Build Status](https://dev.azure.com/kengwang/HyPlayer/_apis/build/status/HyPlayer.HyPlayer?branchName=master)](https://dev.azure.com/kengwang/HyPlayer/_build/latest?definitionId=29&branchName=master)

Azure 编译包会编译 Commit 并发布 Release: [查看Release](https://github.com/HyPlayer/HyPlayer/releases/tag/azure-build)

## 界面预览


## 自动编译状态

| 平台 | Azure DevOps                                                 | AppVeyor |
| ---- | ------------------------------------------------------------ | -------- |
| 状态 | [![Build Status](https://dev.azure.com/kengwang/HyPlayer/_apis/build/status/HyPlayer.HyPlayer?branchName=master)](https://dev.azure.com/kengwang/HyPlayer/_build/latest?definitionId=29&branchName=master) | 暂无     |

## 隐私策略

使用此应用即代表您同意 [网易云音乐隐私策略](https://st.music.163.com/official-terms/privacy#) 以及 [HyPlayer 隐私策略](PrivacyPolicy.md)

## 相关说明

### 软件性质

本软件非盈利性软件,且遵循 [**GPL-v3**](LICENCE) 协议,请勿将此软件用于商业用途.

本软件仅学习交流使用. 如有侵权,请发 Issue 提出.

因为作者忙于学业,通常只会在周末处理相关事情

### 关于无版权

你可以通过使用 [UnblockNeteaseMusic](https://github.com/nondanee/UnblockNeteaseMusic) 进行解灰, 打开后在 HyPlayer 设置页面填入代理服务器地址.

~~在使用代理服务器前,你可能需要解除 UWP 网络环回限制~~

```powershell
CheckNetIsolation LoopbackExempt -a -n="48848aaaaaaccd.hyplayer_fkcggvf9kbkw0"
```
我们已经在2.0.27版本中修复此问题

## 使用

* NeteaseCloudMusicApi
  [wwh1004/NeteaseCloudMusicApi](https://github.com/wwh1004/NeteaseCloudMusicApi) => [HyPlayer/NeteaseCloudMusicApi](https://github.com/HyPlayer/NeteaseCloudMusicApi) (MIT Licence)
* Kawazu [Cutano/Kawazu](https://github.com/Cutano/Kawazu) => [HyPlayer / Kawazu](https://github.com/HyPlayer/Kawazu) (MIT Licence)
* Windows UI Library [microsoft/microsoft-ui-xaml](https://github.com/microsoft/microsoft-ui-xaml) (MIT Licence)
* Windows Community Toolkit [CommunityToolkit/WindowsCommunityToolkit](https://github.com/CommunityToolkit/WindowsCommunityToolkit) (MIT Licence)
* TagLibSharp [mono/taglib-sharp](https://github.com/mono/taglib-sharp) (LGPL)

## 代码参考

* NLyric [wwh1004/NLyric](https://github.com/wwh1004/NLyric)
* ncmdump [anonymous5l/ncmdump-gui](https://github.com/anonymous5l/ncmdump-gui)

## 捐助

爱发电: https://afdian.net/@kengwang

以太坊：0xad13286a373221672a4b4fdb6607eb2d2aa5966a

感谢您的支持!
