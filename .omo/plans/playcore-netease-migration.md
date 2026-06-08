# PlayCore & NeteaseProvider 完全迁移计划

## TL;DR

> **Quick Summary**: 将 HyPlayer 主应用中所有旧的 NC* 领域模型（NCPlayList, NCArtist, NCAlbum, NCUser 等）和 Infrastructure/Netease 代码完全移除，统一使用 PlayCore.Abstraction 的 ProvidableItem 体系和 NeteaseProvider 的模型。
> 
> **Deliverables**:
> - IAuthService 接口和实现改用 PlayCore 类型（NeteaseUser 替代 NCUser，NeteasePlaylist 替代 NCPlayList）
> - 所有 ViewModel 移除对旧 NC* 模型的依赖
> - 删除 Infrastructure/Netease 目录（Api.cs, Mapper.cs, NeteaseTypeIds.cs, PersonalFM.cs 等）
> - 删除 Domain/Music 下的旧模型文件
> - NeteaseProvider 补全 GetProvidableItemByIdAsync 对所有类型的支持
> - PlayCore.Abstraction 补全 ContainersContainer 相关接口
> 
> **Estimated Effort**: Large
> **Parallel Execution**: YES - 4 waves
> **Critical Path**: Task 1 → Task 5 → Task 9 → Task 13 → F1-F4

---

## Context

### Original Request
将 HyPlayer 中原有的播放核心和网易云音乐相关的完全迁移到 PlayCore 和 NeteaseProvider 中。不要保留对原始的兼容。可以对 PlayCore 和 NeteaseProvider 进行适当的补全实现，但是不要太补全。例如用户可以看成一个 ContainersContainer，包含了创建/收藏的歌单，以及收藏的歌手和专辑等。HyPlayer 全面使用 ProvidableItem 系列，不要有对旧的兼容。

### Interview Summary
**Key Discussions**:
- PlayCore 位于 `E:\HyPlayer\HyPlayer.PlayCore`（独立仓库，非 submodule）
- NeteaseProvider 位于 `E:\HyPlayer\HyPlayer.NeteaseProvider`（git submodule）
- 旧 NC* 模型完全删除，由 NeteaseProvider 模型替代
- ContainersContainer 已存在于 PlayCore.Abstraction 中
- 不需要自动化测试，通过构建验证确认正确性

**Research Findings**:
- PlayCore.Abstraction 定义了完整的类型体系（ProvidableItemBase, SingleSongBase, ContainerBase, ContainersContainer 等）
- NeteaseProvider 已实现 12+ 个 PlayCore 接口
- IAuthService 暴露 NCUser 和 NCPlayList 类型 - 需要改为 PlayCore 类型
- NavigationShellViewModel 将 NeteasePlaylist 映射为 NCPlayList - 需要移除此映射
- Infrastructure/Netease 包含旧的 API 辅助代码和 Mapper - 需要删除
- 约 20+ 文件引用旧的 NC* 模型

### Metis Review
**Identified Gaps** (addressed):
- 无测试覆盖下的迁移风险 → 通过构建验证 + 逐文件迁移降低风险
- 旧模型在 UI 层的深层嵌套使用 → 需要仔细检查每个引用点

---

## Work Objectives

### Core Objective
移除 HyPlayer 主应用中所有旧的 NC* 领域模型和 Netease 基础设施代码，统一使用 PlayCore.Abstraction 的 ProvidableItem 体系和 NeteaseProvider 的模型。

### Concrete Deliverables
- IAuthService 接口改用 NeteaseUser/NeteasePlaylist 类型
- 所有 ViewModel 移除 NC* 模型引用
- 删除 HyPlayer/Infrastructure/Netease 目录
- 删除 HyPlayer/Domain/Music 下的 NC* 模型文件
- NeteaseProvider 补全 GetProvidableItemByIdAsync 对 Artist/Album/User/RadioChannel 的支持

### Definition of Done
- [ ] `msbuild HyPlayer.slnx /p:Configuration=Release /p:RuntimeIdentifier=win-x64 /p:Platform=x64 /p:AppxBundle=Never /p:AppxPackageSigningEnabled=false` 构建成功
- [ ] 所有旧 NC* 模型文件已删除
- [ ] Infrastructure/Netease 目录已删除
- [ ] 无编译错误

### Must Have
- IAuthService.CurrentUser 改为 NeteaseUser 类型
- IAuthService.MySongLists 改为 List<NeteasePlaylist> 类型
- 所有 ViewModel 中的 NCPlayList/NCArtist/NCAlbum/NCUser 引用替换为 NeteaseProvider 模型
- 删除 Mapper.cs 中所有 NC* 映射方法
- NeteaseProvider.GetProvidableItemByIdAsync 支持所有类型

### Must NOT Have (Guardrails)
- 不修改 NeteaseProvider 的 git submodule 结构
- 不修改 PlayCore 的仓库结构
- 不过度补全（不添加新的 provider 接口实现，除非必要）
- 不修改播放策略/过渡策略的核心逻辑
- 不添加自动化测试

---

## Verification Strategy (MANDATORY)

> **ZERO HUMAN INTERVENTION** - ALL verification is agent-executed. No exceptions.

### Test Decision
- **Infrastructure exists**: NO
- **Automated tests**: None
- **Framework**: none

### QA Policy
Every task MUST include agent-executed QA scenarios.
Evidence saved to `.omo/evidence/task-{N}-{scenario-slug}.{ext}`.

- **Build Verification**: Use Bash (msbuild) - Build solution, assert 0 errors
- **Code Search**: Use Grep - Verify no references to deleted types

---

## Execution Strategy

### Parallel Execution Waves

```
Wave 1 (Foundation - PlayCore/NeteaseProvider 补全):
├── Task 1: NeteaseProvider 补全 GetProvidableItemByIdAsync [quick]
├── Task 2: NeteaseProvider 补全 NeteaseUser 实现 ContainersContainer [quick]
├── Task 3: PlayCore.Abstraction 检查 ContainersContainer 接口完整性 [quick]
└── Task 4: 创建迁移工具脚本（查找所有旧模型引用）[quick]

Wave 2 (Core Migration - 接口和模型替换):
├── Task 5: IAuthService 接口迁移 NCUser → NeteaseUser [unspecified-high]
├── Task 6: AuthService 实现迁移 [unspecified-high]
├── Task 7: IPlaylistService 接口清理 NCPlayList 引用 [unspecified-high]
└── Task 8: PlaylistService 实现清理 [unspecified-high]

Wave 3 (ViewModel Migration - UI 层清理):
├── Task 9: NavigationShellViewModel 移除 NC* 映射 [unspecified-high]
├── Task 10: MeViewModel 移除 NC* 映射 [unspecified-high]
├── Task 11: FavoriteViewModel 清理 [unspecified-high]
├── Task 12: SongListViewModel 清理 [unspecified-high]
├── Task 13: 其他 ViewModel 清理（Home, Artist, Album, Radio, Search, Comments） [unspecified-high]
└── Task 14: PlaybackCurrentItemSnapshot 和相关类型清理 [unspecified-high]

Wave 4 (Cleanup - 删除旧代码):
├── Task 15: 删除 Infrastructure/Netease 目录 [quick]
├── Task 16: 删除 Domain/Music 下的旧模型文件 [quick]
├── Task 17: 删除 HyPlayItemType 枚举 [quick]
├── Task 18: 清理 Mapper.cs 和旧的映射代码 [quick]
└── Task 19: 最终构建验证 [quick]

Wave FINAL (After ALL tasks — 4 parallel reviews, then user okay):
├── Task F1: Plan compliance audit (oracle)
├── Task F2: Code quality review (unspecified-high)
├── Task F3: Build verification (unspecified-high)
└── Task F4: Scope fidelity check (deep)
-> Present results -> Get explicit user okay

Critical Path: Task 1 → Task 5 → Task 9 → Task 15 → Task 19 → F1-F4 → user okay
Parallel Speedup: ~60% faster than sequential
Max Concurrent: 6 (Wave 3)
```

### Dependency Matrix

| Task | Depends On | Blocks |
|------|------------|--------|
| 1 | - | 5, 6 |
| 2 | - | 5, 6, 9 |
| 3 | - | 2 |
| 4 | - | 5-18 |
| 5 | 1, 2, 4 | 6, 9, 10 |
| 6 | 5 | 15-18 |
| 7 | 4 | 8 |
| 8 | 7 | 15-18 |
| 9 | 5, 6 | 15-18 |
| 10 | 5, 6 | 15-18 |
| 11 | 4 | 15-18 |
| 12 | 4 | 15-18 |
| 13 | 4 | 15-18 |
| 14 | 4 | 15-18 |
| 15 | 6, 8, 9-14 | 19 |
| 16 | 6, 8, 9-14 | 19 |
| 17 | 6, 8, 9-14 | 19 |
| 18 | 6, 8, 9-14 | 19 |
| 19 | 15-18 | F1-F4 |

### Agent Dispatch Summary

- **Wave 1**: 4 tasks - T1-T4 → `quick`
- **Wave 2**: 4 tasks - T5-T8 → `unspecified-high`
- **Wave 3**: 6 tasks - T9-T14 → `unspecified-high`
- **Wave 4**: 5 tasks - T15-T19 → `quick`
- **FINAL**: 4 tasks - F1 → `oracle`, F2 → `unspecified-high`, F3 → `unspecified-high`, F4 → `deep`

---

## TODOs

- [x] 1. NeteaseProvider 补全 GetProvidableItemByIdAsync

  **What to do**:
  - 在 `HyPlayer.NeteaseProvider/HyPlayer.NeteaseProvider/NeteaseProvider.cs` 的 `GetProvidableItemByIdAsync` 方法中（当前约 313-345 行），补全对以下类型的支持：
    - `NeteaseTypeIds.Artist` → 调用 ArtistDetailApi 获取歌手信息，返回 NeteaseArtist
    - `NeteaseTypeIds.Album` → 调用 AlbumDetailApi 获取专辑信息，返回 NeteaseAlbum
    - `NeteaseTypeIds.User` → 调用 UserDetailApi 获取用户信息，返回 NeteaseUser
    - `NeteaseTypeIds.RadioChannel` → 调用 DjChannelDetailApi 获取电台信息，返回 NeteaseRadioChannel
    - `NeteaseTypeIds.RadioProgram` → 调用 DjProgramDetailApi 获取节目信息，返回 NeteaseRadioProgram
  - 参考现有的 `GetSingleSongById` 和 `GetPlaylistById` 方法的实现模式
  - 使用 `RequestAsync` 调用对应的 API contract
  - 使用对应的 Mapper 将响应转换为 NeteaseProvider 模型

  **Must NOT do**:
  - 不修改现有的 SingleSong 和 Playlist 实现
  - 不添加新的 API contract（使用现有的）
  - 不过度错误处理（保持与现有代码一致的模式）

  **Recommended Agent Profile**:
  - **Category**: `quick`
  - **Skills**: []

  **Parallelization**:
  - **Can Run In Parallel**: YES
  - **Parallel Group**: Wave 1 (with Tasks 2, 3, 4)
  - **Blocks**: Tasks 5, 6
  - **Blocked By**: None

  **References**:
  - `HyPlayer.NeteaseProvider/HyPlayer.NeteaseProvider/NeteaseProvider.cs:313-345` - 现有的 GetProvidableItemByIdAsync 实现
  - `HyPlayer.NeteaseProvider/HyPlayer.NeteaseProvider/NeteaseProvider.cs:470-494` - GetSingleSongById 和 GetPlaylistById 的实现模式
  - `HyPlayer.NeteaseProvider/HyPlayer.NeteaseApi/ApiContracts/Artist/` - 歌手相关 API contracts
  - `HyPlayer.NeteaseProvider/HyPlayer.NeteaseApi/ApiContracts/Album/` - 专辑相关 API contracts
  - `HyPlayer.NeteaseProvider/HyPlayer.NeteaseApi/ApiContracts/User/` - 用户相关 API contracts
  - `HyPlayer.NeteaseProvider/HyPlayer.NeteaseApi/ApiContracts/DjChannel/` - 电台相关 API contracts
  - `HyPlayer.NeteaseProvider/HyPlayer.NeteaseProvider/Mappers/` - 现有的 Mapper 实现

  **Acceptance Criteria**:
  - [ ] GetProvidableItemByIdAsync 对所有 7 种类型都能返回正确的 ProvidableItemBase
  - [ ] 编译通过，无错误

  **QA Scenarios**:
  ```
  Scenario: 编译验证
    Tool: Bash (msbuild)
    Preconditions: 代码已修改
    Steps:
      1. 运行 msbuild HyPlayer.NeteaseProvider/HyPlayer.NeteaseProvider/HyPlayer.NeteaseProvider.csproj
      2. 检查输出中的错误数量
    Expected Result: Build succeeded, 0 errors
    Evidence: .omo/evidence/task-1-build.txt
  ```

  **Commit**: YES
  - Message: `feat(netease-provider): complete GetProvidableItemByIdAsync for all types`
  - Files: `HyPlayer.NeteaseProvider/HyPlayer.NeteaseProvider/NeteaseProvider.cs`
  - Pre-commit: `msbuild HyPlayer.NeteaseProvider/HyPlayer.NeteaseProvider/HyPlayer.NeteaseProvider.csproj`

- [x] 2. NeteaseUser 实现 ContainersContainer 接口

  **What to do**:
  - 在 `HyPlayer.NeteaseProvider/HyPlayer.NeteaseProvider/Models/NeteaseUser.cs` 中，确保 NeteaseUser 继承自 `ContainersContainer`（或 `PersonBase` 并实现 `GetSubContainerAsync`）
  - 实现 `GetSubContainerAsync` 方法，返回用户的歌单列表（创建的 + 收藏的）
  - 使用现有的 `NeteaseUserPlaylistSubContainer` 或创建类似的容器
  - 确保返回的容器能正确区分创建的歌单和收藏的歌单

  **Must NOT do**:
  - 不修改 PlayCore.Abstraction 中的 ContainersContainer 定义
  - 不添加新的 API contract

  **Recommended Agent Profile**:
  - **Category**: `quick`
  - **Skills**: []

  **Parallelization**:
  - **Can Run In Parallel**: YES
  - **Parallel Group**: Wave 1 (with Tasks 1, 3, 4)
  - **Blocks**: Tasks 5, 6, 9
  - **Blocked By**: Task 3

  **References**:
  - `HyPlayer.NeteaseProvider/HyPlayer.NeteaseProvider/Models/NeteaseUser.cs` - 现有的 NeteaseUser 实现
  - `HyPlayer.PlayCore/HyPlayer.PlayCore.Abstraction/Models/Containers/ContainersContainer.cs` - ContainersContainer 基类
  - `HyPlayer.PlayCore/HyPlayer.PlayCore.Abstraction/Models/Containers/PersonBase.cs` - PersonBase 基类
  - `HyPlayer/Shell/Navigation/NavigationShellViewModel.cs:183-257` - 现有的歌单加载逻辑（参考）

  **Acceptance Criteria**:
  - [ ] NeteaseUser 实现 GetSubContainerAsync 方法
  - [ ] 返回的容器包含用户的歌单列表
  - [ ] 编译通过

  **QA Scenarios**:
  ```
  Scenario: 编译验证
    Tool: Bash (msbuild)
    Preconditions: 代码已修改
    Steps:
      1. 运行 msbuild HyPlayer.NeteaseProvider/HyPlayer.NeteaseProvider/HyPlayer.NeteaseProvider.csproj
      2. 检查输出中的错误数量
    Expected Result: Build succeeded, 0 errors
    Evidence: .omo/evidence/task-2-build.txt
  ```

  **Commit**: YES
  - Message: `feat(netease-provider): implement ContainersContainer for NeteaseUser`
  - Files: `HyPlayer.NeteaseProvider/HyPlayer.NeteaseProvider/Models/NeteaseUser.cs`
  - Pre-commit: `msbuild HyPlayer.NeteaseProvider/HyPlayer.NeteaseProvider/HyPlayer.NeteaseProvider.csproj`

- [x] 3. PlayCore.Abstraction 检查 ContainersContainer 接口完整性

  **What to do**:
  - 检查 `HyPlayer.PlayCore/HyPlayer.PlayCore.Abstraction/Models/Containers/ContainersContainer.cs` 的定义
  - 确保 `GetSubContainerAsync` 方法签名正确，能返回 `List<ContainerBase>`
  - 检查是否需要添加 `NeteaseUserPlaylistSubContainer` 相关的基类或接口
  - 如果需要，添加 `ProgressiveLoadingContainer` 基类（用于分页加载歌单）

  **Must NOT do**:
  - 不修改现有的 ContainersContainer 核心逻辑
  - 不添加不必要的接口

  **Recommended Agent Profile**:
  - **Category**: `quick`
  - **Skills**: []

  **Parallelization**:
  - **Can Run In Parallel**: YES
  - **Parallel Group**: Wave 1 (with Tasks 1, 2, 4)
  - **Blocks**: Task 2
  - **Blocked By**: None

  **References**:
  - `HyPlayer.PlayCore/HyPlayer.PlayCore.Abstraction/Models/Containers/ContainersContainer.cs` - ContainersContainer 定义
  - `HyPlayer.PlayCore/HyPlayer.PlayCore.Abstraction/Models/Containers/LinerContainerBase.cs` - LinerContainerBase 定义
  - `HyPlayer.PlayCore/HyPlayer.PlayCore.Abstraction/Models/ContainerBase.cs` - ContainerBase 定义
  - `HyPlayer.NeteaseProvider/HyPlayer.NeteaseProvider/Models/NeteaseSearchContainer.cs` - 参考现有的容器实现

  **Acceptance Criteria**:
  - [ ] ContainersContainer 接口支持 NeteaseUser 的使用场景
  - [ ] 编译通过

  **QA Scenarios**:
  ```
  Scenario: 编译验证
    Tool: Bash (msbuild)
    Preconditions: 代码已修改
    Steps:
      1. 运行 msbuild HyPlayer.PlayCore/HyPlayer.PlayCore.Abstraction/HyPlayer.PlayCore.Abstraction.csproj
      2. 检查输出中的错误数量
    Expected Result: Build succeeded, 0 errors
    Evidence: .omo/evidence/task-3-build.txt
  ```

  **Commit**: YES
  - Message: `fix(playcore): ensure ContainersContainer interface completeness`
  - Files: `HyPlayer.PlayCore/HyPlayer.PlayCore.Abstraction/Models/Containers/`
  - Pre-commit: `msbuild HyPlayer.PlayCore/HyPlayer.PlayCore.Abstraction/HyPlayer.PlayCore.Abstraction.csproj`

- [x] 4. 创建迁移工具脚本（查找所有旧模型引用）

  **What to do**:
  - 创建一个 PowerShell 脚本 `scripts/find-legacy-references.ps1`，用于查找所有旧模型的引用
  - 脚本应搜索以下模式：
    - `using HyPlayer.Domain.Music`（排除 `SimpleListItem`, `SongListQueueScope`, `MusicResource`）
    - `NCPlayList`, `NCArtist`, `NCAlbum`, `NCUser`, `NCRadio`, `NCMlog`, `NCMFile`
    - `HyPlayItemType`
    - `Infrastructure.Netease`
  - 输出每个文件的引用位置和行号
  - 用于后续任务的迁移参考

  **Must NOT do**:
  - 不修改任何源代码
  - 不删除任何文件

  **Recommended Agent Profile**:
  - **Category**: `quick`
  - **Skills**: []

  **Parallelization**:
  - **Can Run In Parallel**: YES
  - **Parallel Group**: Wave 1 (with Tasks 1, 2, 3)
  - **Blocks**: Tasks 5-18
  - **Blocked By**: None

  **References**:
  - `HyPlayer/Domain/Music/` - 旧模型目录
  - `HyPlayer/Infrastructure/Netease/` - 旧基础设施目录

  **Acceptance Criteria**:
  - [ ] 脚本能正确找到所有旧模型引用
  - [ ] 输出格式清晰，包含文件路径和行号

  **QA Scenarios**:
  ```
  Scenario: 脚本执行验证
    Tool: Bash (PowerShell)
    Preconditions: 脚本已创建
    Steps:
      1. 运行 pwsh scripts/find-legacy-references.ps1
      2. 检查输出是否包含预期的引用位置
    Expected Result: 输出包含所有旧模型引用的位置
    Evidence: .omo/evidence/task-4-script-output.txt
  ```

  **Commit**: YES
  - Message: `chore: add legacy reference finder script`
  - Files: `scripts/find-legacy-references.ps1`

- [x] 5. IAuthService 接口迁移 NCUser → NeteaseUser

  **What to do**:
  - 修改 `HyPlayer/Services/Abstractions/IAuthService.cs`：
    - 将 `NCUser? CurrentUser` 改为 `NeteaseUser? CurrentUser`
    - 将 `List<NCPlayList> MySongLists` 改为 `List<NeteasePlaylist> MySongLists`
    - 添加 `using HyPlayer.NeteaseProvider.Models`
    - 移除 `using HyPlayer.Domain.Music`
  - 更新所有引用此接口的文件

  **Must NOT do**:
  - 不修改接口的方法签名（只修改属性类型）
  - 不删除任何事件定义

  **Recommended Agent Profile**:
  - **Category**: `unspecified-high`
  - **Skills**: []

  **Parallelization**:
  - **Can Run In Parallel**: NO
  - **Parallel Group**: Wave 2 (sequential with Task 6)
  - **Blocks**: Tasks 6, 9, 10
  - **Blocked By**: Tasks 1, 2, 4

  **References**:
  - `HyPlayer/Services/Abstractions/IAuthService.cs` - 接口定义
  - `HyPlayer/Services/Authentication/AuthService.cs` - 实现类
  - `HyPlayer/Domain/Music/NCUser.cs` - 旧的 NCUser 模型
  - `HyPlayer/Domain/Music/NCPlayList.cs` - 旧的 NCPlayList 模型
  - `HyPlayer.NeteaseProvider/HyPlayer.NeteaseProvider/Models/NeteaseUser.cs` - 新的 NeteaseUser 模型
  - `HyPlayer.NeteaseProvider/HyPlayer.NeteaseProvider/Models/NeteasePlaylist.cs` - 新的 NeteasePlaylist 模型

  **Acceptance Criteria**:
  - [ ] IAuthService.CurrentUser 类型为 NeteaseUser?
  - [ ] IAuthService.MySongLists 类型为 List<NeteasePlaylist>
  - [ ] 编译通过

  **QA Scenarios**:
  ```
  Scenario: 编译验证
    Tool: Bash (msbuild)
    Preconditions: 接口已修改
    Steps:
      1. 运行 msbuild HyPlayer.slnx /p:Configuration=Release /p:RuntimeIdentifier=win-x64 /p:Platform=x64 /p:AppxBundle=Never /p:AppxPackageSigningEnabled=false
      2. 检查输出中的错误数量
    Expected Result: Build succeeded（可能有后续任务的错误，但接口本身的修改应无错）
    Evidence: .omo/evidence/task-5-build.txt
  ```

  **Commit**: YES
  - Message: `refactor(auth): migrate IAuthService to use PlayCore types`
  - Files: `HyPlayer/Services/Abstractions/IAuthService.cs`
  - Pre-commit: `msbuild HyPlayer.slnx /p:Configuration=Release /p:RuntimeIdentifier=win-x64 /p:Platform=x64 /p:AppxBundle=Never /p:AppxPackageSigningEnabled=false`

- [x] 6. AuthService 实现迁移

  **What to do**:
  - 修改 `HyPlayer/Services/Authentication/AuthService.cs`：
    - 将 `CurrentUser` 类型改为 `NeteaseUser?`
    - 将 `MySongLists` 类型改为 `List<NeteasePlaylist>`
    - 移除 `MapProviderUserAsync` 方法（不再需要映射到 NCUser）
    - 更新 `CompleteLoginAsync` 方法，直接使用 NeteaseUser
    - 更新 `LoadMyLikelistAsync` 方法（如果需要）
    - 移除所有对 `NCUser` 和 `NCPlayList` 的引用
  - 确保 `LikeSongCoreAsync` 方法仍然正常工作（它使用 NeteaseSong）

  **Must NOT do**:
  - 不修改登录流程的核心逻辑
  - 不修改 Cookie 管理逻辑

  **Recommended Agent Profile**:
  - **Category**: `unspecified-high`
  - **Skills**: []

  **Parallelization**:
  - **Can Run In Parallel**: NO
  - **Parallel Group**: Wave 2 (after Task 5)
  - **Blocks**: Tasks 15-18
  - **Blocked By**: Task 5

  **References**:
  - `HyPlayer/Services/Authentication/AuthService.cs` - 实现类
  - `HyPlayer/Services/Abstractions/IAuthService.cs` - 接口定义（Task 5 修改后）
  - `HyPlayer.NeteaseProvider/HyPlayer.NeteaseProvider/Models/NeteaseUser.cs` - NeteaseUser 模型
  - `HyPlayer.NeteaseProvider/HyPlayer.NeteaseProvider/Models/NeteasePlaylist.cs` - NeteasePlaylist 模型

  **Acceptance Criteria**:
  - [ ] AuthService 实现新的 IAuthService 接口
  - [ ] 无编译错误
  - [ ] 登录流程逻辑保持不变

  **QA Scenarios**:
  ```
  Scenario: 编译验证
    Tool: Bash (msbuild)
    Preconditions: AuthService 已修改
    Steps:
      1. 运行 msbuild HyPlayer.slnx /p:Configuration=Release /p:RuntimeIdentifier=win-x64 /p:Platform=x64 /p:AppxBundle=Never /p:AppxPackageSigningEnabled=false
      2. 检查输出中的错误数量
    Expected Result: Build succeeded
    Evidence: .omo/evidence/task-6-build.txt
  ```

  **Commit**: YES
  - Message: `refactor(auth): migrate AuthService implementation to use PlayCore types`
  - Files: `HyPlayer/Services/Authentication/AuthService.cs`
  - Pre-commit: `msbuild HyPlayer.slnx /p:Configuration=Release /p:RuntimeIdentifier=win-x64 /p:Platform=x64 /p:AppxBundle=Never /p:AppxPackageSigningEnabled=false`

- [x] 7. IPlaylistService 接口清理 NCPlayList 引用

  **What to do**:
  - 检查 `HyPlayer/Services/Abstractions/IPlaylistService.cs`
  - 确保没有直接引用 `NCPlayList` 或其他旧模型
  - 如果有引用，替换为 `SingleSongBase` 或 `ProvidableItemBase`
  - 检查 `AppendNcSourceAsync` 和 `AppendSourceByKindAsync` 方法的签名

  **Must NOT do**:
  - 不修改播放列表的核心功能
  - 不删除必要的方法

  **Recommended Agent Profile**:
  - **Category**: `unspecified-high`
  - **Skills**: []

  **Parallelization**:
  - **Can Run In Parallel**: YES
  - **Parallel Group**: Wave 2 (with Tasks 5, 6, 8)
  - **Blocks**: Task 8
  - **Blocked By**: Task 4

  **References**:
  - `HyPlayer/Services/Abstractions/IPlaylistService.cs` - 接口定义
  - `HyPlayer/Services/Playback/PlaylistService/PlaylistService.cs` - 实现类

  **Acceptance Criteria**:
  - [ ] IPlaylistService 无旧模型引用
  - [ ] 编译通过

  **QA Scenarios**:
  ```
  Scenario: 编译验证
    Tool: Bash (msbuild)
    Preconditions: 接口已清理
    Steps:
      1. 运行 msbuild HyPlayer.slnx /p:Configuration=Release /p:RuntimeIdentifier=win-x64 /p:Platform=x64 /p:AppxBundle=Never /p:AppxPackageSigningEnabled=false
      2. 检查输出中的错误数量
    Expected Result: Build succeeded
    Evidence: .omo/evidence/task-7-build.txt
  ```

  **Commit**: YES
  - Message: `refactor(playlist): clean up IPlaylistService interface`
  - Files: `HyPlayer/Services/Abstractions/IPlaylistService.cs`

- [x] 8. PlaylistService 实现清理

  **What to do**:
  - 检查 `HyPlayer/Services/Playback/PlaylistService/` 下的所有文件
  - 确保没有直接引用 `NCPlayList` 或其他旧模型
  - 检查 `PlaylistService.Netease.cs` 中的 `AppendNcSongBatches` 方法
  - 确保所有方法都使用 `SingleSongBase` 或 `ProvidableItemBase`

  **Must NOT do**:
  - 不修改播放策略逻辑
  - 不修改过渡策略逻辑

  **Recommended Agent Profile**:
  - **Category**: `unspecified-high`
  - **Skills**: []

  **Parallelization**:
  - **Can Run In Parallel**: YES
  - **Parallel Group**: Wave 2 (with Tasks 5, 6, 7)
  - **Blocks**: Tasks 15-18
  - **Blocked By**: Task 7

  **References**:
  - `HyPlayer/Services/Playback/PlaylistService/PlaylistService.cs` - 主实现
  - `HyPlayer/Services/Playback/PlaylistService/PlaylistService.Netease.cs` - Netease 特定逻辑
  - `HyPlayer/Services/Playback/PlaylistService/PlaylistService.Navigation.cs` - 导航逻辑
  - `HyPlayer/Services/Playback/PlaylistService/PlaylistService.Internal.cs` - 内部辅助

  **Acceptance Criteria**:
  - [ ] PlaylistService 无旧模型引用
  - [ ] 编译通过

  **QA Scenarios**:
  ```
  Scenario: 编译验证
    Tool: Bash (msbuild)
    Preconditions: 实现已清理
    Steps:
      1. 运行 msbuild HyPlayer.slnx /p:Configuration=Release /p:RuntimeIdentifier=win-x64 /p:Platform=x64 /p:AppxBundle=Never /p:AppxPackageSigningEnabled=false
      2. 检查输出中的错误数量
    Expected Result: Build succeeded
    Evidence: .omo/evidence/task-8-build.txt
  ```

  **Commit**: YES
  - Message: `refactor(playlist): clean up PlaylistService implementation`
  - Files: `HyPlayer/Services/Playback/PlaylistService/`

- [x] 9. NavigationShellViewModel 移除 NC* 映射

  **What to do**:
  - 修改 `HyPlayer/Shell/Navigation/NavigationShellViewModel.cs`：
    - 移除 `MapToNCPlayList` 方法
    - 更新 `LoadPlaylistsAsync` 方法，直接使用 `NeteasePlaylist` 而不是映射到 `NCPlayList`
    - 更新 `_auth.MySongLists` 的使用（现在是 `List<NeteasePlaylist>`）
    - 移除对 `NCUser` 的引用
  - 确保导航栏的歌单加载逻辑仍然正常工作

  **Must NOT do**:
  - 不修改导航栏的 UI 结构
  - 不删除必要的导航节点

  **Recommended Agent Profile**:
  - **Category**: `unspecified-high`
  - **Skills**: []

  **Parallelization**:
  - **Can Run In Parallel**: YES
  - **Parallel Group**: Wave 3 (with Tasks 10, 11, 12, 13, 14)
  - **Blocks**: Tasks 15-18
  - **Blocked By**: Tasks 5, 6

  **References**:
  - `HyPlayer/Shell/Navigation/NavigationShellViewModel.cs` - ViewModel 实现
  - `HyPlayer/Services/Abstractions/IAuthService.cs` - 更新后的接口
  - `HyPlayer.NeteaseProvider/HyPlayer.NeteaseProvider/Models/NeteasePlaylist.cs` - NeteasePlaylist 模型
  - `HyPlayer.NeteaseProvider/HyPlayer.NeteaseProvider/Models/NeteaseUser.cs` - NeteaseUser 模型

  **Acceptance Criteria**:
  - [ ] NavigationShellViewModel 无旧模型引用
  - [ ] 歌单加载逻辑仍然正常工作
  - [ ] 编译通过

  **QA Scenarios**:
  ```
  Scenario: 编译验证
    Tool: Bash (msbuild)
    Preconditions: ViewModel 已修改
    Steps:
      1. 运行 msbuild HyPlayer.slnx /p:Configuration=Release /p:RuntimeIdentifier=win-x64 /p:Platform=x64 /p:AppxBundle=Never /p:AppxPackageSigningEnabled=false
      2. 检查输出中的错误数量
    Expected Result: Build succeeded
    Evidence: .omo/evidence/task-9-build.txt
  ```

  **Commit**: YES
  - Message: `refactor(navigation): remove legacy NC* model mappings`
  - Files: `HyPlayer/Shell/Navigation/NavigationShellViewModel.cs`

- [x] 10. MeViewModel 移除 NC* 映射

  **What to do**:
  - 修改 `HyPlayer/Features/User/MeViewModel.cs`：
    - 移除 `MapUser` 和 `MapPlaylist` 方法
    - 更新 `InitializeUserInfo` 方法，直接使用 `NeteaseUser`
    - 更新 `LoadPlayList` 方法，直接使用 `NeteasePlaylist`
    - 移除对 `NCUser` 和 `NCPlayList` 的引用
  - 确保用户页面的逻辑仍然正常工作

  **Must NOT do**:
  - 不修改页面的 UI 结构

  **Recommended Agent Profile**:
  - **Category**: `unspecified-high`
  - **Skills**: []

  **Parallelization**:
  - **Can Run In Parallel**: YES
  - **Parallel Group**: Wave 3 (with Tasks 9, 11, 12, 13, 14)
  - **Blocks**: Tasks 15-18
  - **Blocked By**: Tasks 5, 6

  **References**:
  - `HyPlayer/Features/User/MeViewModel.cs` - ViewModel 实现
  - `HyPlayer/Services/Abstractions/IAuthService.cs` - 更新后的接口
  - `HyPlayer.NeteaseProvider/HyPlayer.NeteaseProvider/Models/NeteaseUser.cs` - NeteaseUser 模型
  - `HyPlayer.NeteaseProvider/HyPlayer.NeteaseProvider/Models/NeteasePlaylist.cs` - NeteasePlaylist 模型

  **Acceptance Criteria**:
  - [ ] MeViewModel 无旧模型引用
  - [ ] 用户页面逻辑仍然正常工作
  - [ ] 编译通过

  **QA Scenarios**:
  ```
  Scenario: 编译验证
    Tool: Bash (msbuild)
    Preconditions: ViewModel 已修改
    Steps:
      1. 运行 msbuild HyPlayer.slnx /p:Configuration=Release /p:RuntimeIdentifier=win-x64 /p:Platform=x64 /p:AppxBundle=Never /p:AppxPackageSigningEnabled=false
      2. 检查输出中的错误数量
    Expected Result: Build succeeded
    Evidence: .omo/evidence/task-10-build.txt
  ```

  **Commit**: YES
  - Message: `refactor(user): remove legacy NC* model mappings from MeViewModel`
  - Files: `HyPlayer/Features/User/MeViewModel.cs`

- [x] 11. FavoriteViewModel 清理

  **What to do**:
  - 检查 `HyPlayer/Features/Library/FavoriteViewModel.cs`
  - 确保没有直接引用 `NCPlayList` 或其他旧模型
  - 如果有引用，替换为 `NeteaseProvider` 模型

  **Must NOT do**:
  - 不修改收藏页面的核心功能

  **Recommended Agent Profile**:
  - **Category**: `unspecified-high`
  - **Skills**: []

  **Parallelization**:
  - **Can Run In Parallel**: YES
  - **Parallel Group**: Wave 3 (with Tasks 9, 10, 12, 13, 14)
  - **Blocks**: Tasks 15-18
  - **Blocked By**: Task 4

  **References**:
  - `HyPlayer/Features/Library/FavoriteViewModel.cs` - ViewModel 实现

  **Acceptance Criteria**:
  - [ ] FavoriteViewModel 无旧模型引用
  - [ ] 编译通过

  **QA Scenarios**:
  ```
  Scenario: 编译验证
    Tool: Bash (msbuild)
    Preconditions: ViewModel 已清理
    Steps:
      1. 运行 msbuild HyPlayer.slnx /p:Configuration=Release /p:RuntimeIdentifier=win-x64 /p:Platform=x64 /p:AppxBundle=Never /p:AppxPackageSigningEnabled=false
      2. 检查输出中的错误数量
    Expected Result: Build succeeded
    Evidence: .omo/evidence/task-11-build.txt
  ```

  **Commit**: YES
  - Message: `refactor(favorite): clean up legacy model references`
  - Files: `HyPlayer/Features/Library/FavoriteViewModel.cs`

- [x] 12. SongListViewModel 清理

  **What to do**:
  - 检查 `HyPlayer/Features/Playlist/SongListViewModel.cs`
  - 确保没有直接引用 `NCPlayList` 或其他旧模型
  - 如果有引用，替换为 `NeteaseProvider` 模型

  **Must NOT do**:
  - 不修改歌单页面的核心功能

  **Recommended Agent Profile**:
  - **Category**: `unspecified-high`
  - **Skills**: []

  **Parallelization**:
  - **Can Run In Parallel**: YES
  - **Parallel Group**: Wave 3 (with Tasks 9, 10, 11, 13, 14)
  - **Blocks**: Tasks 15-18
  - **Blocked By**: Task 4

  **References**:
  - `HyPlayer/Features/Playlist/SongListViewModel.cs` - ViewModel 实现

  **Acceptance Criteria**:
  - [ ] SongListViewModel 无旧模型引用
  - [ ] 编译通过

  **QA Scenarios**:
  ```
  Scenario: 编译验证
    Tool: Bash (msbuild)
    Preconditions: ViewModel 已清理
    Steps:
      1. 运行 msbuild HyPlayer.slnx /p:Configuration=Release /p:RuntimeIdentifier=win-x64 /p:Platform=x64 /p:AppxBundle=Never /p:AppxPackageSigningEnabled=false
      2. 检查输出中的错误数量
    Expected Result: Build succeeded
    Evidence: .omo/evidence/task-12-build.txt
  ```

  **Commit**: YES
  - Message: `refactor(playlist): clean up legacy model references in SongListViewModel`
  - Files: `HyPlayer/Features/Playlist/SongListViewModel.cs`

- [x] 13. 其他 ViewModel 和 UI 层清理

  **What to do**:
  - 检查并清理以下文件中的旧模型引用：
    - `HyPlayer/UI/Lists/SongListItemViewModel.cs`（18 处引用，使用了 NCAlbum、NCArtist、HyPlayItemType）
    - `HyPlayer/Services/Navigation/AppNavigator.cs`（4 处引用，使用了 NCPlayList、NCUser）
    - `HyPlayer/Features/Home/HomePage.xaml.cs`（4 处引用）
    - `HyPlayer/UI/Lists/PlaylistItem.xaml.cs`（2 处引用）
    - `HyPlayer/UI/Dialogs/ArtistSelectDialog.xaml.cs`（2 处引用）
    - `HyPlayer/Features/Home/HomeViewModel.cs`
    - `HyPlayer/Features/Artist/ArtistPageViewModel.cs`
    - `HyPlayer/Features/Album/AlbumPageViewModel.cs`
    - `HyPlayer/Features/Radio/RadioPage.xaml.cs`
    - `HyPlayer/Shell/Search/ShellSearchViewModel.cs`
    - `HyPlayer/Features/Comments/Comments.xaml.cs`
    - `HyPlayer/Features/Playlist/SongListDetail.xaml.cs`
    - `HyPlayer/UI/Playback/PlayBar.xaml.cs`
    - `HyPlayer/Features/Album/AlbumPage.xaml.cs`
    - `HyPlayer/Domain/UserDisplay.cs`
    - `HyPlayer/UI/Comments/SingleComment.xaml.cs`
    - `HyPlayer/Shell/ExpandedPlayer/ExpandedPlayer.xaml.cs`
    - `HyPlayer/Domain/Comments/Comment.cs`
    - `HyPlayer/Infrastructure/Diagnostics/DumpInfo.cs`
  - 将所有 `NCPlayList`, `NCArtist`, `NCAlbum`, `NCUser`, `HyPlayItemType` 引用替换为 `NeteaseProvider` 模型或 PlayCore 类型
  - 移除所有 `MapToNC*` 映射方法

  **Must NOT do**:
  - 不修改页面的核心功能

  **Recommended Agent Profile**:
  - **Category**: `unspecified-high`
  - **Skills**: []

  **Parallelization**:
  - **Can Run In Parallel**: YES
  - **Parallel Group**: Wave 3 (with Tasks 9, 10, 11, 12, 14)
  - **Blocks**: Tasks 15-18
  - **Blocked By**: Task 4

  **References**:
  - `HyPlayer/UI/Lists/SongListItemViewModel.cs` - 18 处旧模型引用
  - `HyPlayer/Services/Navigation/AppNavigator.cs` - 4 处旧模型引用
  - `HyPlayer/Features/Home/HomePage.xaml.cs` - 4 处旧模型引用
  - `HyPlayer/UI/Lists/PlaylistItem.xaml.cs` - 2 处旧模型引用
  - `HyPlayer/UI/Dialogs/ArtistSelectDialog.xaml.cs` - 2 处旧模型引用
  - `HyPlayer/Features/Home/HomeViewModel.cs`
  - `HyPlayer/Features/Artist/ArtistPageViewModel.cs`
  - `HyPlayer/Features/Album/AlbumPageViewModel.cs`
  - `HyPlayer/Features/Radio/RadioPage.xaml.cs`
  - `HyPlayer/Shell/Search/ShellSearchViewModel.cs`
  - `HyPlayer/Features/Comments/Comments.xaml.cs`
  - `HyPlayer/Features/Playlist/SongListDetail.xaml.cs`
  - `HyPlayer/UI/Playback/PlayBar.xaml.cs`
  - `HyPlayer/Features/Album/AlbumPage.xaml.cs`
  - `HyPlayer/Domain/UserDisplay.cs`
  - `HyPlayer/UI/Comments/SingleComment.xaml.cs`
  - `HyPlayer/Shell/ExpandedPlayer/ExpandedPlayer.xaml.cs`
  - `HyPlayer/Domain/Comments/Comment.cs`
  - `HyPlayer/Infrastructure/Diagnostics/DumpInfo.cs`

  **Acceptance Criteria**:
  - [ ] 所有列出的文件无旧模型引用
  - [ ] 编译通过

  **QA Scenarios**:
  ```
  Scenario: 编译验证
    Tool: Bash (msbuild)
    Preconditions: 所有 ViewModel 已清理
    Steps:
      1. 运行 msbuild HyPlayer.slnx /p:Configuration=Release /p:RuntimeIdentifier=win-x64 /p:Platform=x64 /p:AppxBundle=Never /p:AppxPackageSigningEnabled=false
      2. 检查输出中的错误数量
    Expected Result: Build succeeded
    Evidence: .omo/evidence/task-13-build.txt
  ```

  **Commit**: YES
  - Message: `refactor(viewmodels): remove legacy NC* model references from all ViewModels`
  - Files: 多个 ViewModel 文件

- [x] 14. PlaybackCurrentItemSnapshot 和相关类型清理

  **What to do**:
  - 检查 `HyPlayer/Services/Abstractions/PlaybackCurrentItemSnapshot.cs`
  - 确保没有直接引用 `NCPlayList` 或其他旧模型
  - 检查 `HyPlayer/Services/Playback/PlaybackStateService.cs`
  - 确保所有属性都使用 `SingleSongBase` 或 `ProvidableItemBase`

  **Must NOT do**:
  - 不修改播放状态的核心逻辑

  **Recommended Agent Profile**:
  - **Category**: `unspecified-high`
  - **Skills**: []

  **Parallelization**:
  - **Can Run In Parallel**: YES
  - **Parallel Group**: Wave 3 (with Tasks 9, 10, 11, 12, 13)
  - **Blocks**: Tasks 15-18
  - **Blocked By**: Task 4

  **References**:
  - `HyPlayer/Services/Abstractions/PlaybackCurrentItemSnapshot.cs`
  - `HyPlayer/Services/Playback/PlaybackStateService.cs`

  **Acceptance Criteria**:
  - [ ] PlaybackCurrentItemSnapshot 无旧模型引用
  - [ ] 编译通过

  **QA Scenarios**:
  ```
  Scenario: 编译验证
    Tool: Bash (msbuild)
    Preconditions: 类型已清理
    Steps:
      1. 运行 msbuild HyPlayer.slnx /p:Configuration=Release /p:RuntimeIdentifier=win-x64 /p:Platform=x64 /p:AppxBundle=Never /p:AppxPackageSigningEnabled=false
      2. 检查输出中的错误数量
    Expected Result: Build succeeded
    Evidence: .omo/evidence/task-14-build.txt
  ```

  **Commit**: YES
  - Message: `refactor(playback): clean up legacy model references in playback types`
  - Files: `HyPlayer/Services/Abstractions/PlaybackCurrentItemSnapshot.cs`

- [x] 15. 删除 Infrastructure/Netease 目录

  **What to do**:
  - 删除 `HyPlayer/Infrastructure/Netease/` 目录下的所有文件：
    - `Api.cs`
    - `CloudUpload.cs`
    - `ListenTogetherManager.cs`
    - `Mapper.cs`
    - `NeteaseTypeIds.cs`
    - `PersonalFM.cs`
    - `The163KeyHelper.cs`
  - 确保没有其他文件引用这些文件中的类或方法
  - 如果有引用，先在前序任务中迁移

  **Must NOT do**:
  - 不删除其他目录的文件

  **Recommended Agent Profile**:
  - **Category**: `quick`
  - **Skills**: []

  **Parallelization**:
  - **Can Run In Parallel**: YES
  - **Parallel Group**: Wave 4 (with Tasks 16, 17, 18, 19)
  - **Blocks**: Task 19
  - **Blocked By**: Tasks 6, 8, 9-14

  **References**:
  - `HyPlayer/Infrastructure/Netease/` - 目标目录

  **Acceptance Criteria**:
  - [ ] Infrastructure/Netease 目录已删除
  - [ ] 编译通过

  **QA Scenarios**:
  ```
  Scenario: 目录删除验证
    Tool: Bash (PowerShell)
    Preconditions: 前序任务已完成
    Steps:
      1. 运行 Test-Path HyPlayer/Infrastructure/Netease
      2. 检查返回值
    Expected Result: False（目录不存在）
    Evidence: .omo/evidence/task-15-delete.txt
  ```

  **Commit**: YES
  - Message: `chore: remove legacy Netease infrastructure directory`
  - Files: `HyPlayer/Infrastructure/Netease/` (deleted)

- [x] 16. 删除 Domain/Music 下的旧模型文件

  **What to do**:
  - 删除 `HyPlayer/Domain/Music/` 下的旧模型文件：
    - `NCPlayList.cs`
    - `NCArtist.cs`
    - `NCAlbum.cs`
    - `NCUser.cs`
    - `NCRadio.cs`
    - `NCMlog.cs`
    - `NCMFile.cs`
    - `HyPlayItemType.cs`
  - 保留以下文件（非旧模型）：
    - `SimpleListItem.cs`
    - `SongListQueueScope.cs`
    - `MusicResource.cs`

  **Must NOT do**:
  - 不删除 `SimpleListItem.cs`, `SongListQueueScope.cs`, `MusicResource.cs`

  **Recommended Agent Profile**:
  - **Category**: `quick`
  - **Skills**: []

  **Parallelization**:
  - **Can Run In Parallel**: YES
  - **Parallel Group**: Wave 4 (with Tasks 15, 17, 18, 19)
  - **Blocks**: Task 19
  - **Blocked By**: Tasks 6, 8, 9-14

  **References**:
  - `HyPlayer/Domain/Music/` - 目标目录

  **Acceptance Criteria**:
  - [ ] 旧模型文件已删除
  - [ ] SimpleListItem.cs, SongListQueueScope.cs, MusicResource.cs 保留
  - [ ] 编译通过

  **QA Scenarios**:
  ```
  Scenario: 文件删除验证
    Tool: Bash (PowerShell)
    Preconditions: 前序任务已完成
    Steps:
      1. 运行 Test-Path HyPlayer/Domain/Music/NCPlayList.cs
      2. 检查返回值
    Expected Result: False（文件不存在）
    Evidence: .omo/evidence/task-16-delete.txt
  ```

  **Commit**: YES
  - Message: `chore: remove legacy NC* domain model files`
  - Files: `HyPlayer/Domain/Music/NC*.cs`, `HyPlayer/Domain/Music/HyPlayItemType.cs` (deleted)

- [x] 17. 删除 HyPlayItemType 枚举

  **What to do**:
  - 确保 `HyPlayItemType` 枚举已随 Task 16 一起删除
  - 检查是否有其他文件引用此枚举
  - 如果有引用，替换为 PlayCore 的类型系统（ProviderId + TypeId）

  **Must NOT do**:
  - 不创建新的枚举替代

  **Recommended Agent Profile**:
  - **Category**: `quick`
  - **Skills**: []

  **Parallelization**:
  - **Can Run In Parallel**: YES
  - **Parallel Group**: Wave 4 (with Tasks 15, 16, 18, 19)
  - **Blocks**: Task 19
  - **Blocked By**: Tasks 6, 8, 9-14

  **References**:
  - `HyPlayer/Domain/Music/HyPlayItemType.cs` - 目标文件

  **Acceptance Criteria**:
  - [ ] HyPlayItemType 枚举已删除
  - [ ] 无编译错误

  **QA Scenarios**:
  ```
  Scenario: 编译验证
    Tool: Bash (msbuild)
    Preconditions: 枚举已删除
    Steps:
      1. 运行 msbuild HyPlayer.slnx /p:Configuration=Release /p:RuntimeIdentifier=win-x64 /p:Platform=x64 /p:AppxBundle=Never /p:AppxPackageSigningEnabled=false
      2. 检查输出中的错误数量
    Expected Result: Build succeeded
    Evidence: .omo/evidence/task-17-build.txt
  ```

  **Commit**: YES
  - Message: `chore: remove HyPlayItemType enum`
  - Files: `HyPlayer/Domain/Music/HyPlayItemType.cs` (deleted)

- [x] 18. 清理 Mapper.cs 和旧的映射代码

  **What to do**:
  - 确保 `HyPlayer/Infrastructure/Netease/Mapper.cs` 已随 Task 15 一起删除
  - 检查是否有其他文件包含旧的映射方法（如 `MapToNCPlayList`, `MapToNCArtist` 等）
  - 如果有，删除这些方法

  **Must NOT do**:
  - 不删除 NeteaseProvider 中的 Mapper

  **Recommended Agent Profile**:
  - **Category**: `quick`
  - **Skills**: []

  **Parallelization**:
  - **Can Run In Parallel**: YES
  - **Parallel Group**: Wave 4 (with Tasks 15, 16, 17, 19)
  - **Blocks**: Task 19
  - **Blocked By**: Tasks 6, 8, 9-14

  **References**:
  - `HyPlayer/Infrastructure/Netease/Mapper.cs` - 目标文件

  **Acceptance Criteria**:
  - [ ] 所有旧的映射方法已删除
  - [ ] 编译通过

  **QA Scenarios**:
  ```
  Scenario: 编译验证
    Tool: Bash (msbuild)
    Preconditions: 映射代码已清理
    Steps:
      1. 运行 msbuild HyPlayer.slnx /p:Configuration=Release /p:RuntimeIdentifier=win-x64 /p:Platform=x64 /p:AppxBundle=Never /p:AppxPackageSigningEnabled=false
      2. 检查输出中的错误数量
    Expected Result: Build succeeded
    Evidence: .omo/evidence/task-18-build.txt
  ```

  **Commit**: YES
  - Message: `chore: remove legacy mapper code`
  - Files: `HyPlayer/Infrastructure/Netease/Mapper.cs` (deleted)

- [x] 19. 最终构建验证

  **What to do**:
  - 运行完整的解决方案构建
  - 验证所有项目都能成功编译
  - 运行 `grep` 搜索确认没有旧模型引用残留
  - 生成最终的迁移报告

  **Must NOT do**:
  - 不修改任何代码

  **Recommended Agent Profile**:
  - **Category**: `quick`
  - **Skills**: []

  **Parallelization**:
  - **Can Run In Parallel**: NO
  - **Parallel Group**: Wave 4 (after Tasks 15-18)
  - **Blocks**: F1-F4
  - **Blocked By**: Tasks 15-18

  **References**:
  - 所有前序任务的输出

  **Acceptance Criteria**:
  - [ ] 解决方案构建成功，0 错误
  - [ ] 无旧模型引用残留

  **QA Scenarios**:
  ```
  Scenario: 完整构建验证
    Tool: Bash (msbuild)
    Preconditions: 所有迁移任务已完成
    Steps:
      1. 运行 msbuild HyPlayer.slnx /p:Configuration=Release /p:RuntimeIdentifier=win-x64 /p:Platform=x64 /p:AppxBundle=Never /p:AppxPackageSigningEnabled=false
      2. 检查输出中的错误数量
    Expected Result: Build succeeded, 0 errors
    Evidence: .omo/evidence/task-19-final-build.txt

  Scenario: 旧模型引用检查
    Tool: Bash (grep)
    Preconditions: 构建已通过
    Steps:
      1. 运行 grep -r "NCPlayList\|NCArtist\|NCAlbum\|NCUser\|NCRadio\|HyPlayItemType" --include="*.cs" HyPlayer/
      2. 检查输出
    Expected Result: No matches found
    Evidence: .omo/evidence/task-19-grep.txt
  ```

  **Commit**: NO（验证任务，无代码变更）

---

## Final Verification Wave (MANDATORY — after ALL implementation tasks)

> 4 review agents run in PARALLEL. ALL must APPROVE. Present consolidated results to user and get explicit "okay" before completing.

- [x] F1. **Plan Compliance Audit** — `oracle`
  Read the plan end-to-end. For each "Must Have": verify implementation exists (read file, run command). For each "Must NOT Have": search codebase for forbidden patterns — reject with file:line if found. Check evidence files exist in .omo/evidence/. Compare deliverables against plan.
  Output: `Must Have [N/N] | Must NOT Have [N/N] | Tasks [N/N] | VERDICT: APPROVE/REJECT`

- [x] F2. **Code Quality Review** — `unspecified-high`
  Run `msbuild HyPlayer.slnx /p:Configuration=Release /p:RuntimeIdentifier=win-x64 /p:Platform=x64 /p:AppxBundle=Never /p:AppxPackageSigningEnabled=false`. Review all changed files for: `as any`/`@ts-ignore`, empty catches, console.log in prod, commented-out code, unused imports. Check AI slop: excessive comments, over-abstraction, generic names.
  Output: `Build [PASS/FAIL] | Files [N clean/N issues] | VERDICT`

- [x] F3. **Build Verification** — `unspecified-high`
  Build the entire solution. Verify 0 errors. Verify no warnings related to missing types. Run `grep -r "NCPlayList\|NCArtist\|NCAlbum\|NCUser\|NCRadio\|HyPlayItemType" --include="*.cs"` in HyPlayer/ directory to verify no old model references remain.
  Output: `Build [PASS/FAIL] | Old References [0/N found] | VERDICT`

- [x] F4. **Scope Fidelity Check** — `deep`
  For each task: read "What to do", read actual diff (git log/diff). Verify 1:1 — everything in spec was built (no missing), nothing beyond spec was built (no creep). Check "Must NOT do" compliance. Detect cross-task contamination. Flag unaccounted changes.
  Output: `Tasks [N/N compliant] | Contamination [CLEAN/N issues] | Unaccounted [CLEAN/N files] | VERDICT`

---

## Commit Strategy

- **Wave 1**: `feat(netease-provider): complete GetProvidableItemByIdAsync for all types` - NeteaseProvider files
- **Wave 2**: `refactor(auth): migrate IAuthService to use PlayCore types` - AuthService files
- **Wave 3**: `refactor(viewmodels): remove legacy NC* model dependencies` - ViewModel files
- **Wave 4**: `chore: remove legacy Netease infrastructure and domain models` - Deleted files

---

## Success Criteria

### Verification Commands
```bash
msbuild HyPlayer.slnx /p:Configuration=Release /p:RuntimeIdentifier=win-x64 /p:Platform=x64 /p:AppxBundle=Never /p:AppxPackageSigningEnabled=false  # Expected: Build succeeded
grep -r "NCPlayList\|NCArtist\|NCAlbum\|NCUser\|NCRadio\|HyPlayItemType" --include="*.cs" HyPlayer/  # Expected: No matches
```

### Final Checklist
- [ ] All "Must Have" present
- [ ] All "Must NOT Have" absent
- [ ] Build succeeds with 0 errors
- [ ] No references to old NC* models remain
