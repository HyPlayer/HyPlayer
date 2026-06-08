# Legacy Cleanup: NeteaseTypeIds 统一与死代码删除

## TL;DR

> **Quick Summary**: 清理 HyPlayer 中的遗留兼容层 — 删除死代码、统一重复的 NeteaseTypeIds、修复文档注释错误。
> 
> **Deliverables**:
> - 删除 Domain/Comments/Comment.cs（死代码）
> - 删除 Infrastructure/Netease/NeteaseTypeIds.cs（重复定义）
> - Api.cs 迁移到 NeteaseProvider.Constants.NeteaseTypeIds
> - 修复 NeteaseProvider 版本的 Artist/Album 文档注释交换错误
> 
> **Estimated Effort**: Small
> **Parallel Execution**: YES - 2 waves
> **Critical Path**: Task 1 → Task 3 → F1-F4

---

## Context

### Original Request
清理 HyPlayer 中的遗留兼容层，包括删除死代码、统一 NeteaseTypeIds、移除硬编码 typeId 字符串。

### Interview Summary
**Key Discussions**:
- Domain/Comments/Comment.cs 是死代码（零消费者）
- MapCommentTypeId 方法从未被调用
- 两个 NeteaseTypeIds 类几乎完全相同
- 14 个文件使用 Infrastructure.Netease 命名空间，但只有 Api.cs 使用 NeteaseTypeIds
- QueueSourcePrefixes 与 NeteaseTypeIds 是不同的概念（故意不同）
- Domain 层的硬编码字符串（"ns", "rd", "sa" 等）是队列路由前缀，不是 Netease API typeId

**Research Findings**:
- Infrastructure.Netease.NeteaseTypeIds 有 16 个常量 + MapCommentTypeId
- NeteaseProvider.Constants.NeteaseTypeIds 有 17 个常量（包含 Comment="cm"）
- 两个版本的 16 个共享常量名称和值完全相同
- Artist/Album 的 XML 文档注释是交换的（bug）

### Metis Review
**Identified Gaps** (addressed):
- "14 个文件需要迁移" 的说法是错误的 — 只有 Api.cs 使用 NeteaseTypeIds
- Domain 层硬编码字符串不应替换为 NeteaseTypeIds（不同的 ID 方案）
- QueueSourcePrefixes 是不同的概念，不应统一

---

## Work Objectives

### Core Objective
删除死代码，统一重复的 NeteaseTypeIds 定义，修复文档注释错误。

### Concrete Deliverables
- 删除 Domain/Comments/Comment.cs
- 删除 Infrastructure/Netease/NeteaseTypeIds.cs
- Api.cs 迁移到 NeteaseProvider.Constants.NeteaseTypeIds
- 修复 Artist/Album 文档注释

### Definition of Done
- [ ] `dotnet build HyPlayer/HyPlayer.csproj` 构建成功
- [ ] Domain/Comments/Comment.cs 已删除
- [ ] Infrastructure/Netease/NeteaseTypeIds.cs 已删除

### Must Have
- 删除 Domain/Comments/Comment.cs（死代码）
- 删除 Infrastructure/Netease/NeteaseTypeIds.cs（重复定义）
- Api.cs 使用 NeteaseProvider.Constants.NeteaseTypeIds

### Must NOT Have (Guardrails)
- 不删除 Infrastructure/Netease/ 中的其他文件（Api.cs, NCMFile.cs, CloudUpload.cs 等）
- 不修改 Domain 层的硬编码字符串（AppRoute.cs, MusicResource.cs, SongListQueueScope.cs）
- 不修改 QueueSourcePrefixes
- 不修改 CommentTarget.cs 的硬编码字符串
- 不修改 14 个文件的 `using HyPlayer.Infrastructure.Netease` 语句

---

## Verification Strategy

> **ZERO HUMAN INTERVENTION** - ALL verification is agent-executed.

### QA Policy
- **Build Verification**: `dotnet build HyPlayer/HyPlayer.csproj` — 0 errors
- **Code Search**: Verify no references to deleted types

---

## Execution Strategy

### Parallel Execution Waves

```
Wave 1 (Cleanup):
├── Task 1: 删除 Domain/Comments/Comment.cs [quick]
├── Task 2: 删除 Infrastructure/Netease/NeteaseTypeIds.cs + 迁移 Api.cs [quick]
└── Task 3: 修复 NeteaseProvider NeteaseTypeIds 文档注释 [quick]

Wave 2 (Verification):
└── Task 4: 最终构建验证 [quick]

Wave FINAL:
├── Task F1: Plan compliance audit (unspecified-high)
├── Task F2: Build verification (unspecified-high)
└── Task F3: Scope fidelity check (unspecified-high)
-> Present results -> Get explicit user okay

Critical Path: Task 2 → Task 4 → F1-F3
```

### Dependency Matrix

| Task | Depends On | Blocks |
|------|------------|--------|
| 1 | - | 4 |
| 2 | - | 4 |
| 3 | - | 4 |
| 4 | 1, 2, 3 | F1-F3 |

---

## TODOs

- [x] 1. 删除 Domain/Comments/Comment.cs

  **What to do**:
  - 删除 `HyPlayer/Domain/Comments/Comment.cs` 文件
  - 验证没有任何 .cs 文件引用 `Comment` 类（不包括 `CommentTarget`、`NeteaseComment` 等）

  **Must NOT do**:
  - 不删除 CommentTarget.cs
  - 不删除 CommentUserInfo.cs

  **Recommended Agent Profile**:
  - **Category**: `quick`
  - **Skills**: []

  **Parallelization**:
  - **Can Run In Parallel**: YES
  - **Parallel Group**: Wave 1 (with Tasks 2, 3)
  - **Blocks**: Task 4
  - **Blocked By**: None

  **References**:
  - `HyPlayer/Domain/Comments/Comment.cs` - 目标文件（死代码）

  **Acceptance Criteria**:
  - [ ] Comment.cs 已删除
  - [ ] 无编译错误

  **QA Scenarios**:
  ```
  Scenario: 编译验证
    Tool: Bash (dotnet build)
    Steps:
      1. 运行 dotnet build HyPlayer/HyPlayer.csproj
      2. 检查输出中的错误数量
    Expected Result: Build succeeded, 0 errors
    Evidence: .omo/evidence/legacy-task-1-build.txt
  ```

  **Commit**: YES
  - Message: `chore: remove dead Comment class`
  - Files: `HyPlayer/Domain/Comments/Comment.cs` (deleted)

- [x] 2. 删除 Infrastructure/Netease/NeteaseTypeIds.cs + 迁移 Api.cs

  **What to do**:
  - 在 `HyPlayer/Infrastructure/Netease/Api.cs` 中添加 `using HyPlayer.NeteaseProvider.Constants;`
  - 确保 `Api.cs` 中的 `NeteaseTypeIds.SingleSong` 引用仍然编译
  - 删除 `HyPlayer/Infrastructure/Netease/NeteaseTypeIds.cs`
  - 删除 `HyPlayer/Infrastructure/Netease/NeteaseTypeIds.cs` 中的 `using HyPlayer.NeteaseApi.Models;`（如果存在）

  **Must NOT do**:
  - 不删除 Infrastructure/Netease/ 中的其他文件
  - 不修改 14 个文件的 `using HyPlayer.Infrastructure.Netease` 语句

  **Recommended Agent Profile**:
  - **Category**: `quick`
  - **Skills**: []

  **Parallelization**:
  - **Can Run In Parallel**: YES
  - **Parallel Group**: Wave 1 (with Tasks 1, 3)
  - **Blocks**: Task 4
  - **Blocked By**: None

  **References**:
  - `HyPlayer/Infrastructure/Netease/NeteaseTypeIds.cs` - 要删除的文件
  - `HyPlayer/Infrastructure/Netease/Api.cs` - 需要添加 using 的文件

  **Acceptance Criteria**:
  - [ ] NeteaseTypeIds.cs 已删除
  - [ ] Api.cs 编译通过
  - [ ] 无编译错误

  **QA Scenarios**:
  ```
  Scenario: 编译验证
    Tool: Bash (dotnet build)
    Steps:
      1. 运行 dotnet build HyPlayer/HyPlayer.csproj
      2. 检查输出中的错误数量
    Expected Result: Build succeeded, 0 errors
    Evidence: .omo/evidence/legacy-task-2-build.txt
  ```

  **Commit**: YES
  - Message: `chore: remove duplicate NeteaseTypeIds, migrate Api.cs`
  - Files: `HyPlayer/Infrastructure/Netease/NeteaseTypeIds.cs` (deleted), `HyPlayer/Infrastructure/Netease/Api.cs`

- [x] 3. 修复 NeteaseProvider NeteaseTypeIds 文档注释

  **What to do**:
  - 修复 `HyPlayer.NeteaseProvider/HyPlayer.NeteaseProvider/Constants/NeteaseTypeIds.cs` 中的文档注释：
    - `Artist = "ar"` 的注释从 "专辑" 改为 "歌手"
    - `Album = "al"` 的注释从 "歌手" 改为 "专辑"

  **Must NOT do**:
  - 不修改常量值

  **Recommended Agent Profile**:
  - **Category**: `quick`
  - **Skills**: []

  **Parallelization**:
  - **Can Run In Parallel**: YES
  - **Parallel Group**: Wave 1 (with Tasks 1, 2)
  - **Blocks**: Task 4
  - **Blocked By**: None

  **References**:
  - `HyPlayer.NeteaseProvider/HyPlayer.NeteaseProvider/Constants/NeteaseTypeIds.cs` - 目标文件

  **Acceptance Criteria**:
  - [ ] Artist 注释为 "歌手"
  - [ ] Album 注释为 "专辑"

  **QA Scenarios**:
  ```
  Scenario: 注释验证
    Tool: Grep
    Steps:
      1. 检查 NeteaseTypeIds.cs 中 Artist 和 Album 的注释
    Expected Result: Artist 注释为 "歌手"，Album 注释为 "专辑"
    Evidence: .omo/evidence/legacy-task-3-verify.txt
  ```

  **Commit**: YES
  - Message: `fix: correct swapped Artist/Album doc comments in NeteaseTypeIds`
  - Files: `HyPlayer.NeteaseProvider/HyPlayer.NeteaseProvider/Constants/NeteaseTypeIds.cs`

- [x] 4. 最终构建验证

  **What to do**:
  - 运行完整构建
  - 验证删除的文件无引用残留

  **Must NOT do**:
  - 不修改任何代码

  **Recommended Agent Profile**:
  - **Category**: `quick`
  - **Skills**: []

  **Parallelization**:
  - **Can Run In Parallel**: NO
  - **Parallel Group**: Wave 2 (after Tasks 1, 2, 3)
  - **Blocks**: F1-F3
  - **Blocked By**: Tasks 1, 2, 3

  **Acceptance Criteria**:
  - [ ] 构建成功，0 错误
  - [ ] 无旧文件引用残留

  **QA Scenarios**:
  ```
  Scenario: 完整构建验证
    Tool: Bash (dotnet build)
    Steps:
      1. 运行 dotnet build HyPlayer/HyPlayer.csproj
      2. 检查输出中的错误数量
    Expected Result: Build succeeded, 0 errors
    Evidence: .omo/evidence/legacy-task-4-build.txt
  ```

  **Commit**: NO

---

## Final Verification Wave

- [x] F1. **Plan Compliance Audit** — `unspecified-high`
  Read the plan end-to-end. For each "Must Have": verify implementation exists. For each "Must NOT Have": search codebase for forbidden patterns.
  Output: `Must Have [N/N] | Must NOT Have [N/N] | VERDICT: APPROVE/REJECT`

- [x] F2. **Build Verification** — `unspecified-high`
  Build the entire solution. Verify 0 errors.
  Output: `Build [PASS/FAIL] | VERDICT`

- [x] F3. **Scope Fidelity Check** — `unspecified-high`
  For each task: read "What to do", read actual diff. Verify 1:1. Check "Must NOT do" compliance.
  Output: `Tasks [N/N compliant] | VERDICT`

---

## Commit Strategy

- **Wave 1**: `chore: remove dead code and unify NeteaseTypeIds`

---

## Success Criteria

### Verification Commands
```bash
dotnet build HyPlayer/HyPlayer.csproj  # Expected: Build succeeded
grep -r "class Comment[^T]" --include="*.cs" HyPlayer/Domain/Comments/  # Expected: No matches (Comment.cs deleted)
```

### Final Checklist
- [ ] All "Must Have" present
- [ ] All "Must NOT Have" absent
- [ ] Build succeeds with 0 errors
