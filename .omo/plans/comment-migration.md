# Comment 系统迁移到 PlayCore

## TL;DR

> **Quick Summary**: 将 HyPlayer 的 Comment 系统迁移到 PlayCore 的 CommentBase 抽象。NeteaseProvider 实现 ICommentProvidable 和 IProvidableItemCommentProvidable，HyPlayer 移除对 NeteaseApi 的直接依赖。
> 
> **Deliverables**:
> - NeteaseProvider 实现 ICommentProvidable 和 IProvidableItemCommentProvidable
> - 创建 NeteaseComment : CommentBase 模型
> - CommentTarget 改为使用 typeId 字符串（移除 NeteaseResourceType 依赖）
> - Comments.xaml.cs 和 SingleComment.xaml.cs 移除 MapProviderComment 桥接
> - 移除重复的 MapCommentTypeId 方法
> 
> **Estimated Effort**: Medium
> **Parallel Execution**: YES - 3 waves
> **Critical Path**: Task 1 → Task 3 → Task 5 → F1-F4

---

## Context

### Original Request
将 HyPlayer 中的 Comment 系统迁移到 PlayCore 的 CommentBase 抽象，NeteaseProvider 实现 ICommentProvidable 和 IProvidableItemCommentProvidable。

### Interview Summary
**Key Discussions**:
- PlayCore 已有 CommentBase、ICommentProvidable、IProvidableItemCommentProvidable
- NeteaseApi 已有 CommentsApi、CommentLikeApi、CommentFloorApi、CommentDto
- App.xaml.cs 已注册接口到 NeteaseProvider，但 NeteaseProvider 未实现这些接口
- Comments.xaml.cs 已使用 IProvidableItemCommentProvidable，但仍映射到旧 Comment 类

**Research Findings**:
- CommentBase 只有 Content, SendDate, Sender(PersonBase), LikedCount
- 旧 Comment 类有 HasLiked, ReplyCount, IsMainComment, ResourceId, ResourceType
- CommentTarget 使用 NeteaseResourceType（直接依赖 NeteaseApi）
- CommentUserInfo 是轻量级 POCO（无 required 成员，适合 XAML 绑定）
- PersonBase 继承自 ProvidableItemBase（有 required 成员 Name, ActualId）
- 6 个文件使用 CommentTarget 创建导航参数

### Metis Review
**Identified Gaps** (addressed):
- CommentBase 缺少 HasLiked, ReplyCount 等 UI 需要的属性 → 保留 Comment 作为适配器
- PersonBase 有 required 成员导致 XAML 问题 → 保留 CommentUserInfo 作为轻量级适配器
- CommentFloorApi 使用 Time 值分页而非 offset → 通过 NextOffset 传递
- 两个重复的 MapCommentTypeId 方法 → 统一到一个地方

---

## Work Objectives

### Core Objective
将 Comment 系统迁移到 PlayCore 抽象，NeteaseProvider 实现评论接口，HyPlayer 移除对 NeteaseApi 的直接依赖。

### Concrete Deliverables
- NeteaseComment : CommentBase 模型
- NeteaseProvider 实现 ICommentProvidable 和 IProvidableItemCommentProvidable
- CommentTarget 改为使用 typeId 字符串
- Comments.xaml.cs 和 SingleComment.xaml.cs 移除 MapProviderComment 桥接
- 移除重复的 MapCommentTypeId 方法

### Definition of Done
- [ ] `dotnet build HyPlayer/HyPlayer.csproj` 构建成功
- [ ] NeteaseProvider 实现 ICommentProvidable 和 IProvidableItemCommentProvidable
- [ ] CommentTarget 不再依赖 NeteaseResourceType

### Must Have
- NeteaseComment : CommentBase 模型（包含 ReplyCount, HasLiked 等扩展属性）
- NeteaseProvider 实现 GetCommentsAsync, GetThreadedCommentsAsync, SetCommentLikeStateAsync
- CommentTarget 使用 typeId 字符串而非 NeteaseResourceType
- 统一的 MapCommentTypeId 方法

### Must NOT Have (Guardrails)
- 不修改 PlayCore（CommentBase, ICommentProvidable, IProvidableItemCommentProvidable）
- 不修改 NeteaseApi（CommentDto, CommentsApi, CommentFloorApi, CommentLikeApi）
- 不实现 PostCommentAsync（当前禁用）
- 不添加头像加载到 CommentBase.Sender
- 不添加新的 NuGet 依赖

---

## Verification Strategy

> **ZERO HUMAN INTERVENTION** - ALL verification is agent-executed.

### Test Decision
- **Infrastructure exists**: NO
- **Automated tests**: None
- **Framework**: none

### QA Policy
Every task MUST include agent-executed QA scenarios.
Evidence saved to `.omo/evidence/task-{N}-{scenario-slug}.{ext}`.

- **Build Verification**: Use Bash (dotnet build) - Build project, assert 0 errors
- **Code Search**: Use Grep - Verify no references to deleted types

---

## Execution Strategy

### Parallel Execution Waves

```
Wave 1 (NeteaseProvider Implementation):
├── Task 1: 创建 NeteaseComment 模型 [quick]
├── Task 2: NeteaseProvider 实现 ICommentProvidable [unspecified-high]
└── Task 3: NeteaseProvider 实现 IProvidableItemCommentProvidable [unspecified-high]

Wave 2 (HyPlayer Migration):
├── Task 4: CommentTarget 迁移到 typeId 字符串 [unspecified-high]
├── Task 5: Comments.xaml.cs 移除 MapProviderComment 桥接 [unspecified-high]
├── Task 6: SingleComment.xaml.cs 迁移 [unspecified-high]
└── Task 7: 统一 MapCommentTypeId 方法 [quick]

Wave 3 (Cleanup):
└── Task 8: 最终构建验证 [quick]

Wave FINAL (After ALL tasks):
├── Task F1: Plan compliance audit (oracle)
├── Task F2: Code quality review (unspecified-high)
├── Task F3: Build verification (unspecified-high)
└── Task F4: Scope fidelity check (deep)
-> Present results -> Get explicit user okay

Critical Path: Task 1 → Task 3 → Task 5 → Task 8 → F1-F4
Parallel Speedup: ~40% faster than sequential
Max Concurrent: 3 (Wave 1)
```

### Dependency Matrix

| Task | Depends On | Blocks |
|------|------------|--------|
| 1 | - | 2, 3 |
| 2 | 1 | 5, 6 |
| 3 | 1 | 5, 6 |
| 4 | - | 5, 6 |
| 5 | 2, 3, 4 | 8 |
| 6 | 2, 3, 4 | 8 |
| 7 | - | 8 |
| 8 | 5, 6, 7 | F1-F4 |

### Agent Dispatch Summary

- **Wave 1**: 3 tasks - T1-T3 → `quick`, `unspecified-high`
- **Wave 2**: 4 tasks - T4-T7 → `unspecified-high`, `quick`
- **Wave 3**: 1 task - T8 → `quick`
- **FINAL**: 4 tasks - F1 → `oracle`, F2 → `unspecified-high`, F3 → `unspecified-high`, F4 → `deep`

---

## TODOs

- [x] 1. 创建 NeteaseComment 模型

  **What to do**:
  - 在 `HyPlayer.NeteaseProvider/HyPlayer.NeteaseProvider/Models/` 下创建 `NeteaseComment.cs`
  - 继承自 `CommentBase`（来自 PlayCore.Abstraction）
  - 实现 `IHasCover` 接口（提供头像）
  - 添加扩展属性：`ReplyCount`, `HasLiked`, `IsMainComment`, `ResourceId`, `ResourceTypeId`
  - 设置 `ProviderId => "ncm"`, `TypeId => "cm"`
  - 创建 Mapper 将 `CommentDto` 转换为 `NeteaseComment`

  **Must NOT do**:
  - 不修改 PlayCore 的 CommentBase
  - 不修改 NeteaseApi 的 CommentDto

  **Recommended Agent Profile**:
  - **Category**: `quick`
  - **Skills**: []

  **Parallelization**:
  - **Can Run In Parallel**: YES
  - **Parallel Group**: Wave 1 (with Tasks 2, 3)
  - **Blocks**: Tasks 2, 3
  - **Blocked By**: None

  **References**:
  - `HyPlayer.PlayCore/HyPlayer.PlayCore.Abstraction/Models/SingleItems/CommentBase.cs` - 基类
  - `HyPlayer.NeteaseProvider/HyPlayer.NeteaseApi/Models/ResponseModels/CommentDto.cs` - 源 DTO
  - `HyPlayer.NeteaseProvider/HyPlayer.NeteaseProvider/Models/NeteaseUser.cs` - 参考实现模式
  - `HyPlayer.NeteaseProvider/HyPlayer.NeteaseProvider/Mappers/ProfileDataToNeteaseUserMapper.cs` - 参考 Mapper 模式

  **Acceptance Criteria**:
  - [ ] NeteaseComment 类存在，继承 CommentBase
  - [ ] Mapper 将 CommentDto 转换为 NeteaseComment
  - [ ] 编译通过

  **QA Scenarios**:
  ```
  Scenario: 编译验证
    Tool: Bash (dotnet build)
    Preconditions: 代码已修改
    Steps:
      1. 运行 dotnet build HyPlayer.NeteaseProvider/HyPlayer.NeteaseProvider/HyPlayer.NeteaseProvider.csproj
      2. 检查输出中的错误数量
    Expected Result: Build succeeded, 0 errors
    Evidence: .omo/evidence/comment-task-1-build.txt
  ```

  **Commit**: YES
  - Message: `feat(netease-provider): add NeteaseComment model`
  - Files: `HyPlayer.NeteaseProvider/HyPlayer.NeteaseProvider/Models/NeteaseComment.cs`, `HyPlayer.NeteaseProvider/HyPlayer.NeteaseProvider/Mappers/`

- [x] 2. NeteaseProvider 实现 ICommentProvidable

  **What to do**:
  - 在 `NeteaseProvider.cs` 类声明中添加 `ICommentProvidable`
  - 实现 `GetCommentContainerAsync` 方法（返回 null 即可）
  - 实现 `GetCommentsAsync(itemId, typeId, offset, count, ctk)` 方法：
    - 将 typeId 转换为 NeteaseResourceType（使用 TypeIdToResourceIdMapper 的反向映射）
    - 调用 CommentsApi
    - 将 CommentDto[] 转换为 NeteaseComment[]
    - 返回 ProviderPageResult<CommentBase>
  - 实现 `GetThreadedCommentsAsync(itemId, typeId, commentId, offset, count, ctk)` 方法：
    - 调用 CommentFloorApi
    - 返回 ProviderPageResult<CommentBase>
  - 实现 `SetCommentLikeStateAsync(itemId, typeId, commentId, like, ctk)` 方法：
    - 调用 CommentLikeApi

  **Must NOT do**:
  - 不实现 PostCommentAsync（当前禁用）
  - 不修改 NeteaseApi 层

  **Recommended Agent Profile**:
  - **Category**: `unspecified-high`
  - **Skills**: []

  **Parallelization**:
  - **Can Run In Parallel**: NO
  - **Parallel Group**: Wave 1 (after Task 1)
  - **Blocks**: Tasks 5, 6
  - **Blocked By**: Task 1

  **References**:
  - `HyPlayer.PlayCore/HyPlayer.PlayCore.Abstraction/Interfaces/Provider/ICommentProvidable.cs` - 接口定义
  - `HyPlayer.NeteaseProvider/HyPlayer.NeteaseApi/ApiContracts/Comment/CommentsApi.cs` - API
  - `HyPlayer.NeteaseProvider/HyPlayer.NeteaseApi/ApiContracts/Comment/CommentFloorApi.cs` - API
  - `HyPlayer.NeteaseProvider/HyPlayer.NeteaseApi/ApiContracts/Comment/CommentLikeApi.cs` - API
  - `HyPlayer.NeteaseProvider/HyPlayer.NeteaseProvider/Constants/TypeIdToResourceIdMapper.cs` - typeId 映射

  **Acceptance Criteria**:
  - [ ] NeteaseProvider 类声明包含 ICommentProvidable
  - [ ] GetCommentsAsync, GetThreadedCommentsAsync, SetCommentLikeStateAsync 已实现
  - [ ] 编译通过

  **QA Scenarios**:
  ```
  Scenario: 编译验证
    Tool: Bash (dotnet build)
    Preconditions: 代码已修改
    Steps:
      1. 运行 dotnet build HyPlayer.NeteaseProvider/HyPlayer.NeteaseProvider/HyPlayer.NeteaseProvider.csproj
      2. 检查输出中的错误数量
    Expected Result: Build succeeded, 0 errors
    Evidence: .omo/evidence/comment-task-2-build.txt
  ```

  **Commit**: YES
  - Message: `feat(netease-provider): implement ICommentProvidable`
  - Files: `HyPlayer.NeteaseProvider/HyPlayer.NeteaseProvider/NeteaseProvider.cs`

- [x] 3. NeteaseProvider 实现 IProvidableItemCommentProvidable

  **What to do**:
  - 在 `NeteaseProvider.cs` 类声明中添加 `IProvidableItemCommentProvidable`
  - 实现 `GetCommentsAsync(itemId, typeId, offset, count, ctk)` 方法（与 Task 2 相同逻辑）
  - 实现 `GetThreadedCommentsAsync(itemId, typeId, commentId, offset, count, ctk)` 方法
  - 实现 `PostCommentAsync(itemId, typeId, content, replyToCommentId, ctk)` 方法（返回 null）
  - 实现 `SetCommentLikeStateAsync(itemId, typeId, commentId, like, ctk)` 方法

  **Must NOT do**:
  - 不修改 NeteaseApi 层

  **Recommended Agent Profile**:
  - **Category**: `unspecified-high`
  - **Skills**: []

  **Parallelization**:
  - **Can Run In Parallel**: NO
  - **Parallel Group**: Wave 1 (after Task 1)
  - **Blocks**: Tasks 5, 6
  - **Blocked By**: Task 1

  **References**:
  - `HyPlayer.PlayCore/HyPlayer.PlayCore.Abstraction/Interfaces/Provider/IProvidableItemCommentProvidable.cs` - 接口定义

  **Acceptance Criteria**:
  - [ ] NeteaseProvider 类声明包含 IProvidableItemCommentProvidable
  - [ ] 所有方法已实现
  - [ ] 编译通过

  **QA Scenarios**:
  ```
  Scenario: 编译验证
    Tool: Bash (dotnet build)
    Preconditions: 代码已修改
    Steps:
      1. 运行 dotnet build HyPlayer.NeteaseProvider/HyPlayer.NeteaseProvider/HyPlayer.NeteaseProvider.csproj
      2. 检查输出中的错误数量
    Expected Result: Build succeeded, 0 errors
    Evidence: .omo/evidence/comment-task-3-build.txt
  ```

  **Commit**: YES
  - Message: `feat(netease-provider): implement IProvidableItemCommentProvidable`
  - Files: `HyPlayer.NeteaseProvider/HyPlayer.NeteaseProvider/NeteaseProvider.cs`

- [x] 4. CommentTarget 迁移到 typeId 字符串

  **What to do**:
  - 修改 `HyPlayer/Domain/Comments/CommentTarget.cs`：
    - 将 `record CommentTarget(NeteaseResourceType ResourceType, string ResourceId)` 改为 `record CommentTarget(string TypeId, string ResourceId)`
    - 更新所有工厂方法使用 typeId 字符串：
      - `Song(id) => new("sg", id)`
      - `Album(id) => new("al", id)`
      - `Playlist(id) => new("pl", id)`
      - `MV(id) => new("mv", id)`
      - `MLog(id) => new("mb", id)`
      - `RadioProgram(id) => new("pr", id)`
    - 更新 `TryParseExternalResource` 方法
  - 更新所有 6 个调用方文件

  **Must NOT do**:
  - 不修改 NeteaseApi

  **Recommended Agent Profile**:
  - **Category**: `unspecified-high`
  - **Skills**: []

  **Parallelization**:
  - **Can Run In Parallel**: YES
  - **Parallel Group**: Wave 2 (with Tasks 5, 6, 7)
  - **Blocks**: Tasks 5, 6
  - **Blocked By**: None

  **References**:
  - `HyPlayer/Domain/Comments/CommentTarget.cs` - 目标文件
  - `HyPlayer/UI/Playback/PlayBar/PlayBar.xaml.cs` - 调用方
  - `HyPlayer/UI/Lists/SongsList.xaml.cs` - 调用方
  - `HyPlayer/UI/Lists/GroupedSongsListViewModel.cs` - 调用方
  - `HyPlayer/Features/Video/MVPage.xaml.cs` - 调用方
  - `HyPlayer/Features/Playlist/SongListViewModel.cs` - 调用方
  - `HyPlayer/Features/Album/AlbumPageViewModel.cs` - 调用方

  **Acceptance Criteria**:
  - [ ] CommentTarget 使用 typeId 字符串
  - [ ] 所有 6 个调用方已更新
  - [ ] 编译通过

  **QA Scenarios**:
  ```
  Scenario: 编译验证
    Tool: Bash (dotnet build)
    Preconditions: 代码已修改
    Steps:
      1. 运行 dotnet build HyPlayer/HyPlayer.csproj
      2. 检查输出中的错误数量
    Expected Result: Build succeeded, 0 errors
    Evidence: .omo/evidence/comment-task-4-build.txt
  ```

  **Commit**: YES
  - Message: `refactor(comments): migrate CommentTarget to typeId strings`
  - Files: `HyPlayer/Domain/Comments/CommentTarget.cs`, 6 个调用方文件

- [x] 5. Comments.xaml.cs 移除 MapProviderComment 桥接

  **What to do**:
  - 修改 `HyPlayer/Features/Comments/Comments.xaml.cs`：
    - 移除 `MapProviderComment` 方法
    - 直接使用 `CommentBase`（或 `NeteaseComment`）而非映射到 `Comment`
    - 更新 `hotComments` 和 `normalComments` 类型为 `ObservableCollection<CommentBase>`
    - 移除 `MapCommentTypeId` 方法（使用 CommentTarget.TypeId）
    - 更新 `LoadProviderCommentsAsync` 使用 CommentTarget.TypeId
    - 移除 `using HyPlayer.NeteaseApi.Models`

  **Must NOT do**:
  - 不修改 PlayCore

  **Recommended Agent Profile**:
  - **Category**: `unspecified-high`
  - **Skills**: []

  **Parallelization**:
  - **Can Run In Parallel**: NO
  - **Parallel Group**: Wave 2 (after Tasks 2, 3, 4)
  - **Blocks**: Task 8
  - **Blocked By**: Tasks 2, 3, 4

  **References**:
  - `HyPlayer/Features/Comments/Comments.xaml.cs` - 目标文件
  - `HyPlayer/Features/Comments/Comments.xaml` - XAML 绑定

  **Acceptance Criteria**:
  - [ ] Comments.xaml.cs 不再引用 NeteaseResourceType
  - [ ] 不再有 MapProviderComment 方法
  - [ ] 编译通过

  **QA Scenarios**:
  ```
  Scenario: 编译验证
    Tool: Bash (dotnet build)
    Preconditions: 代码已修改
    Steps:
      1. 运行 dotnet build HyPlayer/HyPlayer.csproj
      2. 检查输出中的错误数量
    Expected Result: Build succeeded, 0 errors
    Evidence: .omo/evidence/comment-task-5-build.txt
  ```

  **Commit**: YES
  - Message: `refactor(comments): remove MapProviderComment bridge from Comments page`
  - Files: `HyPlayer/Features/Comments/Comments.xaml.cs`

- [x] 6. SingleComment.xaml.cs 迁移

  **What to do**:
  - 修改 `HyPlayer/UI/Controls/SingleComment.xaml.cs`：
    - 将 `MainCommentProperty` 类型从 `Comment` 改为 `CommentBase`
    - 移除 `MapProviderComment` 方法
    - 更新 `floorComments` 类型为 `ObservableCollection<CommentBase>`
    - 移除 `MapCommentTypeId` 方法（使用统一的方法）
    - 更新 `LoadFloorComments` 使用 CommentBase
    - 更新 `Like_Click` 使用 CommentBase
    - 移除 `using HyPlayer.NeteaseApi.Models`
  - 更新 `SingleComment.xaml` 绑定（如果需要）

  **Must NOT do**:
  - 不修改 PlayCore

  **Recommended Agent Profile**:
  - **Category**: `unspecified-high`
  - **Skills**: []

  **Parallelization**:
  - **Can Run In Parallel**: NO
  - **Parallel Group**: Wave 2 (after Tasks 2, 3, 4)
  - **Blocks**: Task 8
  - **Blocked By**: Tasks 2, 3, 4

  **References**:
  - `HyPlayer/UI/Controls/SingleComment.xaml.cs` - 目标文件
  - `HyPlayer/UI/Controls/SingleComment.xaml` - XAML 绑定
  - `HyPlayer/UI/Lists/CommentsList.xaml.cs` - 依赖此控件
  - `HyPlayer/UI/Lists/CommentsList.xaml` - XAML 绑定

  **Acceptance Criteria**:
  - [ ] SingleComment.xaml.cs 不再引用 NeteaseResourceType
  - [ ] 不再有 MapProviderComment 方法
  - [ ] 编译通过

  **QA Scenarios**:
  ```
  Scenario: 编译验证
    Tool: Bash (dotnet build)
    Preconditions: 代码已修改
    Steps:
      1. 运行 dotnet build HyPlayer/HyPlayer.csproj
      2. 检查输出中的错误数量
    Expected Result: Build succeeded, 0 errors
    Evidence: .omo/evidence/comment-task-6-build.txt
  ```

  **Commit**: YES
  - Message: `refactor(comments): migrate SingleComment to use CommentBase`
  - Files: `HyPlayer/UI/Controls/SingleComment.xaml.cs`, `HyPlayer/UI/Controls/SingleComment.xaml`

- [x] 7. 统一 MapCommentTypeId 方法

  **What to do**:
  - 创建一个共享的工具方法（或扩展方法）将 typeId 字符串转换为 NeteaseResourceType
  - 或者在 NeteaseProvider 中提供一个静态方法
  - 移除 Comments.xaml.cs 和 SingleComment.xaml.cs 中的重复方法

  **Must NOT do**:
  - 不修改 NeteaseApi

  **Recommended Agent Profile**:
  - **Category**: `quick`
  - **Skills**: []

  **Parallelization**:
  - **Can Run In Parallel**: YES
  - **Parallel Group**: Wave 2 (with Tasks 4, 5, 6)
  - **Blocks**: Task 8
  - **Blocked By**: None

  **References**:
  - `HyPlayer/Features/Comments/Comments.xaml.cs:171-185` - 现有方法
  - `HyPlayer/UI/Controls/SingleComment.xaml.cs:134-146` - 现有方法

  **Acceptance Criteria**:
  - [ ] 只有一个 MapCommentTypeId 实现
  - [ ] 编译通过

  **QA Scenarios**:
  ```
  Scenario: 编译验证
    Tool: Bash (dotnet build)
    Preconditions: 代码已修改
    Steps:
      1. 运行 dotnet build HyPlayer/HyPlayer.csproj
      2. 检查输出中的错误数量
    Expected Result: Build succeeded, 0 errors
    Evidence: .omo/evidence/comment-task-7-build.txt
  ```

  **Commit**: YES
  - Message: `refactor(comments): unify MapCommentTypeId method`
  - Files: 视具体实现而定

- [x] 8. 最终构建验证

  **What to do**:
  - 运行完整构建
  - 验证 Comment 系统文件不再引用 NeteaseResourceType
  - 生成最终报告

  **Must NOT do**:
  - 不修改任何代码

  **Recommended Agent Profile**:
  - **Category**: `quick`
  - **Skills**: []

  **Parallelization**:
  - **Can Run In Parallel**: NO
  - **Parallel Group**: Wave 3 (after Tasks 5, 6, 7)
  - **Blocks**: F1-F4
  - **Blocked By**: Tasks 5, 6, 7

  **References**:
  - 所有前序任务的输出

  **Acceptance Criteria**:
  - [ ] 构建成功，0 错误
  - [ ] Comment 系统文件无 NeteaseResourceType 引用

  **QA Scenarios**:
  ```
  Scenario: 完整构建验证
    Tool: Bash (dotnet build)
    Preconditions: 所有迁移任务已完成
    Steps:
      1. 运行 dotnet build HyPlayer/HyPlayer.csproj
      2. 检查输出中的错误数量
    Expected Result: Build succeeded, 0 errors
    Evidence: .omo/evidence/comment-task-8-build.txt

  Scenario: NeteaseResourceType 引用检查
    Tool: Bash (grep)
    Preconditions: 构建已通过
    Steps:
      1. 运行 grep -r "NeteaseResourceType" --include="*.cs" HyPlayer/Domain/Comments/ HyPlayer/Features/Comments/ HyPlayer/UI/Controls/SingleComment.xaml.cs
      2. 检查输出
    Expected Result: No matches found
    Evidence: .omo/evidence/comment-task-8-grep.txt
  ```

  **Commit**: NO

---

## Final Verification Wave

> 4 review agents run in PARALLEL. ALL must APPROVE. Present consolidated results to user and get explicit "okay" before completing.

- [x] F1. **Plan Compliance Audit** — `oracle`
  Read the plan end-to-end. For each "Must Have": verify implementation exists. For each "Must NOT Have": search codebase for forbidden patterns. Check evidence files exist. Compare deliverables against plan.
  Output: `Must Have [N/N] | Must NOT Have [N/N] | Tasks [N/N] | VERDICT: APPROVE/REJECT`

- [x] F2. **Code Quality Review** — `unspecified-high`
  Run `dotnet build HyPlayer/HyPlayer.csproj`. Review all changed files for code quality issues.
  Output: `Build [PASS/FAIL] | Files [N clean/N issues] | VERDICT`

- [x] F3. **Build Verification** — `unspecified-high`
  Build the entire solution. Verify 0 errors. Verify no NeteaseResourceType references in Comment system files.
  Output: `Build [PASS/FAIL] | Old References [0/N found] | VERDICT`

- [x] F4. **Scope Fidelity Check** — `deep`
  For each task: read "What to do", read actual diff. Verify 1:1. Check "Must NOT do" compliance. Detect cross-task contamination.
  Output: `Tasks [N/N compliant] | Contamination [CLEAN/N issues] | VERDICT`

---

## Commit Strategy

- **Wave 1**: `feat(netease-provider): implement comment interfaces` - NeteaseProvider files
- **Wave 2**: `refactor(comments): migrate to PlayCore CommentBase abstraction` - HyPlayer comment files
- **Wave 3**: `chore: final build verification` - No code changes

---

## Success Criteria

### Verification Commands
```bash
dotnet build HyPlayer/HyPlayer.csproj  # Expected: Build succeeded
grep -r "NeteaseResourceType" --include="*.cs" HyPlayer/Domain/Comments/ HyPlayer/Features/Comments/ HyPlayer/UI/Controls/SingleComment.xaml.cs  # Expected: No matches
```

### Final Checklist
- [ ] All "Must Have" present
- [ ] All "Must NOT Have" absent
- [ ] Build succeeds with 0 errors
- [ ] No NeteaseResourceType references in Comment system files
