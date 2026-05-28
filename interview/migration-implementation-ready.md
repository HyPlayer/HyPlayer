---
sessionID: ses_1aca47841ffeNG9IceMjLJvw7c
baseMessageCount: 55
updatedAt: 2026-05-23T06:25:31.645Z
---

# 迁移整个 HyPlayer 项目, 使其不再直接调用网易云音乐 API 以及自己管理播放相关的内容, 使其使用: * PlayCore: E:\HyPlayer\HyPlayer.PlayCore * NeteaseProvider: E:\HyPlayer\HyPlayer.NeteaseProvider (不再直接调用 NeteaseApi 而是使用 ProvidableItem) 可以将 HyPlayer 项目中的部分功能迁移到 PlayCore / Provider 中方便其直接重复使用而不是自己实现 你需要深刻理解 PlayCore 和 Provider 的设计, 尽可能抽象 (例如 用户喜欢某歌曲 和 用户收藏歌曲到某歌单 都可以视作把 ProvidableItem 放入到 用户/歌单 容器中)

## Current spec

访谈结束，迁移规格已明确：先提交当前已构建通过的低风险 Provider 化批次；随后进入 PlayCore foundation 批次，实现兼容 NeteaseApi Results 形状的 Result<T>、ProviderId/TypeId/ActualId/ResourceKind/Quality 缓存 key、GetOrCreateAsync + TTL/Version + 离线 fallback 缓存抽象、按 ProviderId + provider stable account key 的 SessionStore，并补充核心接口测试。后续按可构建批次跨仓库分别 commit，使用 git commit --no-gpg-sign。

## Q&A history

Q: 第一阶段你希望迁移到什么深度？
A: 直接替换 PlaybackStateService/PlaylistService/PlaybackControlService

Q: 哪些网易功能必须优先迁入 NeteaseProvider，而不是继续留在 HyPlayer？
A: 再加上 MV/视频/评论发布等完整功能

Q: 直接替换播放核心时，底层音频后端你希望怎么处理？
A: 用现有 HyPlayer.UWP.Chopin 写 AudioServiceBase 适配器

Q: 这种大范围替换过程中，你能接受短期内哪些功能暂时不可用？
A: 可以接受阶段性大破坏，优先完成架构替换

Q: 旧 UI 模型 HyPlayItem 要如何切换？
A: UI 立刻改用 SingleSongBase/ProvidableItemBase

Q: 旧播放服务被 PlayCore 替换后，旧代码如何处理？
A: 直接删除旧 Playback/Playlist/MediaProvider/Strategy 代码

Q: 这次大重构的验收边界是什么？
A: 要求所有旧功能都恢复到可用

Q: 旧播放历史、当前播放列表、本地设置里保存的 HyPlayItem/Netease song id 数据要怎么处理？
A: 不管了 当旧的不存在!

Q: 最终是否要求 HyPlayer 主项目完全不引用 NeteaseApi 项目？
A: 可以暂时保留少量例外直到 Provider 补全

Q: 所有旧功能都要恢复时，优先恢复顺序怎么排？
A: 播放/歌词/歌单优先，其次登录搜索，再做 MV/评论/一起听/云盘

Q: 是否允许直接修改 E:\HyPlayer\HyPlayer.PlayCore 和 E:\HyPlayer\HyPlayer.NeteaseProvider 这两个外部项目的公共接口？
A: 允许破坏性改接口，HyPlayer 同步适配

Q: 每个迁移批次的最低验证标准是什么？
A: 必须整 solution Release x64 build 通过

Q: 除整 solution Release x64 build 外，是否要求同步补充/更新测试？
A: 需要，PlayCore/NeteaseProvider 的核心接口都要有测试

Q: 迁移过程中是否需要按批次提交 git commit？
A: 每个都需要一个 commit, 但是需要 skip signing

Q: 涉及 HyPlayer、PlayCore、NeteaseProvider 多个仓库/子模块时，commit 应如何组织？
A: 同一批次跨仓库分别 commit，commit message 保持关联

Q: 你说的 skip signing 希望用哪种方式？
A: git commit --no-gpg-sign

Q: 迁移应在当前分支直接推进，还是新建迁移分支？
A: 当前分支直接推进

Q: Provider 完整功能补齐时，倾向哪种 API 设计风格？
A: 保持细粒度接口，每类能力一个接口

Q: UI 立刻切换到 ProvidableItem 时，是否允许同步大改 XAML 绑定和页面 ViewModel 结构？
A: 允许，按新模型彻底改

Q: 本地音乐和 NCM 文件在新模型中如何处理？
A: 实现 LocalProvider/NcmProvider，统一变成 ProvidableItem

Q: LocalProvider/NcmProvider 应放在哪里？
A: 放在 HyPlayer 主项目内，作为 app-specific provider

Q: Provider 操作失败时希望统一怎么表达？
A: 引入 Result<T> 返回错误信息

Q: Result<T> 类型应该定义在哪里？
A: 使用现有 NeteaseApi Results 类型并推广

Q: LocalProvider/NcmProvider 第一版需要覆盖哪些能力？
A: 播放资源 + 元数据/封面 + 本地歌词 + 导入扫描

Q: 现有 NeteaseApi Results 类型推广到 PlayCore 时，是否接受 PlayCore.Abstraction 依赖 NeteaseApi？
A: 不接受，在 PlayCore 新建兼容形状的 Result<T>，再做转换

Q: NCM 文件支持应作为独立 NcmProvider，还是并入 LocalProvider？
A: 并入 LocalProvider，用 TypeId 区分普通本地和 NCM

Q: 歌词解析和逐字/翻译/罗马音处理最终归属哪里？
A: HyPlayer 保留歌词渲染和高级处理，PlayCore 只负责获取

Q: 在线音乐缓存和封面缓存归属哪里？
A: 不应该让 Provider 知道啊, HyPlayer 缓存实现, PlayCore 定义抽象吧

Q: PlayCore 的缓存抽象第一版需要覆盖哪些资源类型？
A: 音频 + 图片封面 + 歌词/raw metadata

Q: Provider 登录态/Cookie 应由谁持久化？
A: PlayCore 定义 SessionStore 抽象，Provider 使用

Q: SessionStore 保存 Cookie/Token 时是否需要加密或使用系统凭据保险箱？
A: 先沿用现有本地设置保存方式

Q: metadata/歌词/封面缓存失效策略第一版怎么定？
A: Provider 返回建议 TTL/版本，HyPlayer 缓存实现执行

Q: 缓存体系是否需要支持离线模式？
A: 需要，离线时尽量从缓存返回

Q: 当前已有一批低风险 Provider 化改动且构建通过，是否先按要求提交一个 batch commit？
A: 是，先提交当前批次

Q: 提交当前批次后，下一批核心替换从哪里开始最合适？
A: 先做 PlayCore Result<T>/缓存/SessionStore 基础抽象

Q: 当前批次 commit message 用什么风格？
A: refactor: replace low-risk netease api calls with provider interfaces

Q: PlayCore 的 Result<T> 第一版需要包含哪些字段？
A: 兼容 NeteaseApi Results：Success/Error 双分支 + Match 风格 API

Q: PlayCore 缓存抽象的 key 应如何设计？
A: ProviderId + TypeId + ActualId + ResourceKind + Quality

Q: 缓存 key 里的 Quality 应该如何标准化？
A: 用 ResourceQualityTag 序列化后的稳定 key

Q: SessionStore 抽象第一版应该按什么粒度保存？
A: 按 ProviderId + AccountId 保存多账号 session

Q: SessionStore 的 AccountId 从哪里来？
A: Provider 自己生成 stable account key

Q: PlayCore 缓存抽象第一版应该提供什么 API？
A: GetOrCreateAsync + TTL/Version + 离线 fallback

Q: 访谈信息已经足够形成下一批实现计划。是否结束访谈并开始执行：先提交当前低风险批次，再做 PlayCore foundation 批次？
A: 是，结束访谈并开始执行
