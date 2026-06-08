# HyPlayer PlayCore / NeteaseProvider / Depository Migration

## TL;DR
> **Summary**: Migrate HyPlayer behind PlayCore + NeteaseProvider abstractions so the app stops directly calling `HyPlayer.NeteaseApi` and stops owning provider/playback-content orchestration. Replace current `CommunityToolkit.Mvvm.DependencyInjection.Ioc` / MSDI-style DI with native Depository 4.0.1 from `E:\Personal\Depository`, explicitly excluding Depository's MSDI compatibility bridge.
> **Deliverables**:
> - Native Depository DI root and registrations for HyPlayer.
> - PlayCore/NeteaseProvider abstractions for content, resources, containers, auth/session, and mutating item/container operations.
> - Temporary `HyPlayItem` compatibility adapters with explicit removal criteria.
> - App migration away from direct `NeteaseApi` calls/imports.
> - Playback queue/resource/lyric/control migration toward PlayCore boundaries while preserving UWP-specific audio surface where required.
> **Effort**: XL
> **Parallel**: YES - 5 waves
> **Critical Path**: Task 1 → Task 2 → Task 4 → Task 6 → Task 9 → Final Verification

## Context
### Original Request
Migrate the whole HyPlayer project so it no longer directly calls NetEase Cloud Music API and no longer self-manages playback-related content. It must use:
- PlayCore: `E:\HyPlayer\HyPlayer.PlayCore`
- NeteaseProvider: `E:\HyPlayer\HyPlayer.NeteaseProvider`
- Provider abstractions such as `ProvidableItem` instead of direct `NeteaseApi` calls.
- Some reusable HyPlayer logic may move into PlayCore / Provider.
- DI must fully use Depository 4.0.1, native API only, no Depository MSDI compatibility bridge.

### Interview Summary
- Architecture-level migration plan only; implementation happens later via `/start-work`.
- Adopt adapter-first migration, not big bang.
- Keep existing `HyPlayItem` UI/playback surface temporarily.
- Target boundary: only `HyPlayer.NeteaseProvider` may reference/call `HyPlayer.NeteaseApi`; HyPlayer app depends on PlayCore/NeteaseProvider abstractions.
- Use local `E:\Personal\Depository` source as authoritative for Depository APIs.

### Metis Review (gaps addressed)
- Added explicit PlayCore-vs-HyPlayer UI model ownership boundary.
- Added adapter exit/removal criteria.
- Added guardrail forbidding MSDI registrations, service-provider wrappers, bridge packages, and `IServiceProvider` escape hatches except platform-required boundaries.
- Added mandatory grep checks for direct NetEase usage.
- Added operation mapping task for search, recommendations, lyrics, stream URL, details, likes, playlist/container edits, auth/session, user library, daily songs, comments.
- Added explicit auth/session boundary decision: NetEase session state belongs to NeteaseProvider/provider context; HyPlayer may store app preferences only.
- Added playback boundary: UWP-specific audio engine may remain app-side until PlayCore has compatible implementation; provider/content/playlist semantics move to PlayCore.

## Work Objectives
### Core Objective
Make HyPlayer consume provider/playback abstractions instead of direct NetEase API and self-owned provider content logic, while standardizing dependency injection on native Depository 4.0.1.

### Deliverables
- Depository package/project references and native DI bootstrap.
- New or extended PlayCore/NeteaseProvider contracts for current app operations.
- NeteaseProvider implementations that encapsulate all NetEase API calls.
- HyPlayer adapters between PlayCore `ProvidableItemBase`/`SingleSongBase`/`ContainerBase` and existing `HyPlayItem`.
- HyPlayer service/viewmodel/page migration to provider abstractions.
- Verification evidence under `.sisyphus/evidence/`.

### Definition of Done (verifiable conditions with commands)
- `grep -R "HyPlayer.NeteaseApi\|NeteaseApis\|NeteaseCloudMusicApiHandler" HyPlayer/` returns no production app usages except allowed comments in migration notes, if any.
- `grep -R "CommunityToolkit.Mvvm.DependencyInjection\|Ioc.Default\|ServiceCollection\|BuildServiceProvider\|Depository.Extensions.DependencyInjection" HyPlayer/` returns no production DI usage.
- Build restore succeeds: `msbuild HyPlayer.slnx /t:Restore /p:Configuration=Release /p:RuntimeIdentifier=win-x64 /p:Platform=x64`.
- Whole solution x64 build succeeds: `msbuild HyPlayer.slnx /p:Configuration=Release /p:RuntimeIdentifier=win-x64 /p:Platform=x64 /p:AppxBundle=Never /p:AppxPackageSigningEnabled=false`.
- Package project x64 build succeeds: `msbuild HyPlayer.Package\HyPlayer.Package.wapproj /p:Configuration=Release /p:UapAppxPackageBuildMode=SideloadOnly /p:AppxBundle=Never /p:Platform=x64 /p:RuntimeIdentifier=win-x64 /p:AppxPackageSigningEnabled=false`.
- NeteaseProvider tests pass using their TUnit/Microsoft.Testing.Platform setup, with required secrets only in local/CI secret injection.

### Must Have
- Native Depository only: `Depository.Core.DepositoryFactory.CreateNew(...)`, `Depository.Extensions.AddSingleton/AddScoped/AddTransient/Add`, `Resolve<T>`, `ResolveMultiple<T>`, `Resolve<IEnumerable<T>>`, scopes.
- Use Depository `IEnumerable<T>` support for multi-provider/multi-strategy registrations.
- Keep `NeteaseApi` inside NeteaseProvider or its tests only.
- Preserve AOT/source-generation constraints; add app-level serialized types to `HyPlayer/Classes/JsonDefaultContext.cs` when needed.
- Keep UWP entry points `HyPlayer/App.xaml` and `HyPlayer/App.xaml.cs` at root.

### Must NOT Have
- No Depository MSDI compatibility bridge (`Depository.Extensions.DependencyInjection`).
- No `CommunityToolkit.Mvvm.DependencyInjection.Ioc.Default` in migrated app code.
- No new direct `NeteaseApis.*` calls in HyPlayer app.
- No new reflection-heavy/dynamic serialization paths without AOT-safe source generation.
- Do not hand-edit generated NeteaseProvider `.g.g.cs` files.

## Verification Strategy
> ZERO HUMAN INTERVENTION - all verification is agent-executed.
- Test decision: tests-after + targeted unit tests where test projects exist; add tests in PlayCore/NeteaseProvider for new abstractions. Main UWP app has no test project, so use build + grep + manual agent QA scenarios.
- QA policy: Every task has agent-executed scenarios.
- Evidence: `.sisyphus/evidence/task-{N}-{slug}.{ext}`.

## Execution Strategy
### Parallel Execution Waves
Wave 1: Task 1, Task 2, Task 3 [foundation, contracts, mapping]
Wave 2: Task 4, Task 5, Task 6 [DI bootstrap, adapters, provider operations]
Wave 3: Task 7, Task 8, Task 9 [replace app NetEase reads/mutations/playback routing]
Wave 4: Task 10, Task 11, Task 12 [UI/code-behind migration, cleanup, tests]
Wave 5: Task 13, Task 14 [build/package verification, adapter exit documentation]

### Dependency Matrix (full, all tasks)
- 1 blocks 2, 4, 7, 8, 9, 10, 11.
- 2 blocks 5, 6, 7, 8, 9.
- 3 blocks 4, 12, 13.
- 4 blocks 10, 11, 13.
- 5 blocks 7, 8, 9, 10.
- 6 blocks 7, 8, 10, 12.
- 7, 8, 9 block 11, 13, 14.
- 10 blocks 11, 13.
- 11, 12 block 13.
- 13 blocks 14 and final verification.

### Agent Dispatch Summary (wave → task count → categories)
- Wave 1 → 3 tasks → deep, ultrabrain, quick
- Wave 2 → 3 tasks → deep, unspecified-high
- Wave 3 → 3 tasks → deep, unspecified-high
- Wave 4 → 3 tasks → unspecified-high, quick
- Wave 5 → 2 tasks → unspecified-high, writing

## TODOs
> Implementation + Test = ONE task. Never separate.
> EVERY task MUST have: Agent Profile + Parallelization + QA Scenarios.

- [x] 1. Create Current-Operation Migration Inventory

  **What to do**: Create an implementation inventory table in code comments or a temporary migration tracking doc under `.sisyphus/evidence/` only during execution, mapping every current HyPlayer operation to target PlayCore/NeteaseProvider abstractions. Include direct NetEase usage from 48 files, especially `HyPlayer/Services/Playback/MediaProviders/NeteaseStreamingProvider.cs`, `CachedNeteaseProvider.cs`, `LyricService.cs`, queue providers, `PersonalFmStrategy.cs`, `AuthService.cs`, `Shell/Search/ShellSearchViewModel.cs`, UI dialogs, `PlayBar.xaml.cs`, `SongsList.xaml.cs`, and `PlaylistItem.xaml.cs`. Map operations: stream URL, lyrics, playlist/album/artist/song details, search suggestions/results, recommendations, personal FM, like/unlike, add/remove playlist item, create/delete/privacy playlist, comment floor/like, login/logout/session, user library.
  **Must NOT do**: Do not change production code in this task except adding evidence files if the executor chooses; do not guess unmapped operations.

  **Recommended Agent Profile**:
  - Category: `deep` - Reason: requires cross-codebase inventory and architecture mapping.
  - Skills: [`dotnet-best-practices`] - C# boundary and migration analysis.
  - Omitted: [`frontend-design`] - no UI design changes.

  **Parallelization**: Can Parallel: YES | Wave 1 | Blocks: [2,4,7,8,9,10,11] | Blocked By: []

  **References**:
  - Pattern: `HyPlayer/Services/Playback/MediaProviders/NeteaseStreamingProvider.cs` - direct stream URL provider to replace.
  - Pattern: `HyPlayer/Services/Playback/LyricService.cs` - lyric loading to move behind `ILyricProvidable`.
  - Pattern: `HyPlayer/Shell/Search/ShellSearchViewModel.cs` - search direct API replacement.
  - Pattern: `HyPlayer/UI/Lists/SongsList.xaml.cs` - playlist/cloud mutation direct API replacement.
  - Pattern: `HyPlayer/UI/Playback/PlayBar/PlayBar.xaml.cs` - personal FM trash/direct API and DI usage.
  - API/Type: `E:/HyPlayer/HyPlayer.PlayCore/HyPlayer.PlayCore.Abstraction/Models/ProvidableItemBase.cs` - universal item identity.
  - API/Type: `E:/HyPlayer/HyPlayer.PlayCore/HyPlayer.PlayCore.Abstraction/Interfaces/Provider/IProvableItemLikable.cs` - heart/add-to-container abstraction.

  **Acceptance Criteria**:
  - [ ] Evidence file `.sisyphus/evidence/task-1-operation-inventory.md` lists every matched HyPlayer direct NetEase file and its target abstraction.
  - [ ] Inventory explicitly marks unsupported operations requiring new Provider/PlayCore contract additions.
  - [ ] Inventory marks auth/session ownership as NeteaseProvider/provider context, not HyPlayer API handler.

  **QA Scenarios**:
  ```
  Scenario: Inventory covers all direct NetEase usages
    Tool: Bash
    Steps: Run grep for "HyPlayer.NeteaseApi|NeteaseApis|NeteaseCloudMusicApiHandler" under HyPlayer/ and compare each result to task-1 inventory.
    Expected: Every grep result is listed with a target abstraction or explicit removal note.
    Evidence: .sisyphus/evidence/task-1-operation-inventory.md

  Scenario: Unsupported operations are not hidden
    Tool: Bash
    Steps: Search inventory for "UNSUPPORTED" or "NEW CONTRACT" markers.
    Expected: All operations lacking existing PlayCore/NeteaseProvider support are marked for Task 2/6.
    Evidence: .sisyphus/evidence/task-1-operation-inventory-gaps.txt
  ```

  **Commit**: NO | Message: `n/a` | Files: [.sisyphus/evidence/task-1-operation-inventory.md]

- [x] 2. Extend PlayCore Contracts for Provider Content Boundaries

  **What to do**: In PlayCore abstraction projects, add only the missing contracts required by Task 1. Use existing abstractions first: `ProvidableItemBase`, `ContainerBase`, `SingleSongBase`, `LinerContainerBase`, `UndeterminedContainerBase`, `IMusicResourceProvidable`, `ILyricProvidable`, `ISearchableProvider`, `IRecommendationProvidable`, `IProvableItemLikable`. For user/library and playlist mutations, model operations as adding/removing `ProvidableItemBase` to/from `ContainerBase` or target container id. Add tests in PlayCore test project for new contract semantics when concrete logic exists.
  **Must NOT do**: Do not add NetEase-specific types to PlayCore; do not move UWP UI models into PlayCore.

  **Recommended Agent Profile**:
  - Category: `ultrabrain` - Reason: core abstraction decisions have long-term impact.
  - Skills: [`dotnet-best-practices`] - API design and AOT-safe C#.
  - Omitted: [`testcontainers`] - no external containers.

  **Parallelization**: Can Parallel: YES | Wave 1 | Blocks: [5,6,7,8,9] | Blocked By: [1 for final coverage]

  **References**:
  - API/Type: `E:/HyPlayer/HyPlayer.PlayCore/HyPlayer.PlayCore.Abstraction/Models/ProvidableItemBase.cs` - identity shape: provider/type/actual id.
  - API/Type: `E:/HyPlayer/HyPlayer.PlayCore/HyPlayer.PlayCore.Abstraction/Models/ContainerBase.cs` - container marker.
  - API/Type: `E:/HyPlayer/HyPlayer.PlayCore/HyPlayer.PlayCore.Abstraction/Interfaces/Provider/IProvableItemLikable.cs` - null target = like; target = add-to-container.
  - Test: `E:/HyPlayer/HyPlayer.PlayCore/HyPlayer.PlayCore.Tests/DefaultPlayListManagerTests.cs` - PlayCore test style.

  **Acceptance Criteria**:
  - [ ] No new PlayCore abstraction references `HyPlayer.NeteaseApi`, UWP, or `HyPlayItem`.
  - [ ] Every new contract has XML comments explaining provider-neutral semantics.
  - [ ] `IProvableItemLikable` semantics are preserved: null target id means user-heart/favorite; non-null target id means add to target container.

  **QA Scenarios**:
  ```
  Scenario: PlayCore remains provider-neutral
    Tool: Bash
    Steps: Run grep for "Netease|HyPlayItem|Windows.UI|Microsoft.UI" under E:\HyPlayer\HyPlayer.PlayCore\HyPlayer.PlayCore.Abstraction.
    Expected: No matches except documentation examples explicitly marked provider-neutral.
    Evidence: .sisyphus/evidence/task-2-playcore-neutrality.txt

  Scenario: Contract tests/build compile
    Tool: Bash
    Steps: Run the PlayCore test project command used by that repository's test setup.
    Expected: PlayCore tests pass or compile with no contract breakage.
    Evidence: .sisyphus/evidence/task-2-playcore-tests.log
  ```

  **Commit**: YES | Message: `refactor(playcore): add provider content boundary contracts` | Files: [E:/HyPlayer/HyPlayer.PlayCore/**]

- [x] 3. Add Native Depository Package/Project References Without MSDI Bridge

  **What to do**: Add Depository 4.0.1 dependency to HyPlayer using either NuGet `Depository` metapackage or local project references as appropriate for the workspace. Use native packages from `E:/Personal/Depository/src/Depository`, `Depository.Core`, `Depository.Extensions`, and `Depository.Abstraction`. Do not reference `Depository.Extensions.DependencyInjection`. Remove/prepare removal of `CommunityToolkit.Mvvm.DependencyInjection` where it is only used for `Ioc`.
  **Must NOT do**: Do not add `Depository.Extensions.DependencyInjection`; do not use `IServiceProvider` as the app container.

  **Recommended Agent Profile**:
  - Category: `quick` - Reason: project reference/package setup with strict guardrails.
  - Skills: [`dotnet-best-practices`] - project file hygiene.
  - Omitted: [`dotnet-aot-compat`] - not resolving IL warnings yet.

  **Parallelization**: Can Parallel: YES | Wave 1 | Blocks: [4,12,13] | Blocked By: []

  **References**:
  - API/Type: `E:/Personal/Depository/src/Depository.Core/DepositoryFactory.cs` - `DepositoryFactory.CreateNew(...)`.
  - API/Type: `E:/Personal/Depository/src/Depository.Extensions/AddDependencyExtension.cs` - native registration extensions.
  - Forbidden: `E:/Personal/Depository/src/Depository.Extensions.DependencyInjection/` - do not use.
  - Project: `HyPlayer/HyPlayer.csproj` - main app dependency target.

  **Acceptance Criteria**:
  - [ ] `HyPlayer/HyPlayer.csproj` references native Depository dependencies and does not reference `Depository.Extensions.DependencyInjection`.
  - [ ] Restore succeeds with repo `nuget.config`.
  - [ ] Project files do not introduce bare cross-platform assumptions.

  **QA Scenarios**:
  ```
  Scenario: Depository bridge is absent
    Tool: Bash
    Steps: Run grep for "Depository.Extensions.DependencyInjection|DepositoryServiceProviderFactory|DepositoryServiceProvider" under HyPlayer/.
    Expected: No production matches.
    Evidence: .sisyphus/evidence/task-3-no-depository-msdi-bridge.txt

  Scenario: Restore accepts dependencies
    Tool: Bash
    Steps: Run msbuild HyPlayer.slnx /t:Restore /p:Configuration=Release /p:RuntimeIdentifier=win-x64 /p:Platform=x64.
    Expected: Restore succeeds.
    Evidence: .sisyphus/evidence/task-3-restore.log
  ```

  **Commit**: YES | Message: `build(app): reference native Depository dependencies` | Files: [HyPlayer/HyPlayer.csproj, optional solution/project reference files]

- [x] 4. Replace App DI Bootstrap With Native Depository Root

  **What to do**: Replace `App.xaml.cs` service provider initialization with native Depository root creation. Create a small app-owned DI access point if needed, e.g. `HyPlayer/App/AppDepository.cs`, exposing `IDepository Root` and typed `Resolve<T>` wrappers. Register all current services/viewmodels with `AddSingleton`, `AddScoped`, `AddTransient`, and use `Resolve<IEnumerable<T>>` / `ResolveMultiple<T>` for multi-strategy/provider collections. Preserve existing lifetimes unless Task 1 proves they are wrong.
  **Must NOT do**: Do not use `ServiceCollection`, `BuildServiceProvider`, `Ioc.Default.ConfigureServices`, or Depository's MSDI bridge.

  **Recommended Agent Profile**:
  - Category: `deep` - Reason: central composition root migration.
  - Skills: [`dotnet-best-practices`] - DI lifetime correctness.
  - Omitted: [`frontend-design`] - no visual changes.

  **Parallelization**: Can Parallel: NO | Wave 2 | Blocks: [10,11,13] | Blocked By: [1,3]

  **References**:
  - Pattern: `HyPlayer/App.xaml.cs` - current `InitializeServices` and composition root.
  - API/Type: `E:/Personal/Depository/src/Depository.Core/DepositoryFactory.cs` - create root.
  - API/Type: `E:/Personal/Depository/src/Depository.Extensions/ResolveExtension.cs` - `Resolve<T>`, `ResolveMultiple<T>`.
  - API/Type: `E:/Personal/Depository/src/Depository.Core/Depository.Resolve.cs:101-125` - `IEnumerable<T>` support.

  **Acceptance Criteria**:
  - [ ] App startup creates exactly one root Depository container.
  - [ ] All existing registered app services/viewmodels are registered with native Depository.
  - [ ] Multi-provider/strategy dependencies use native `IEnumerable<T>` or `ResolveMultiple<T>`, not manual service locator lists unless justified.
  - [ ] Grep finds no `ServiceCollection`/`BuildServiceProvider`/`Ioc.Default.ConfigureServices` in `HyPlayer/`.

  **QA Scenarios**:
  ```
  Scenario: App composition uses native Depository only
    Tool: Bash
    Steps: Run grep for "ServiceCollection|BuildServiceProvider|Ioc.Default.ConfigureServices|Depository.Extensions.DependencyInjection" under HyPlayer/.
    Expected: No production matches.
    Evidence: .sisyphus/evidence/task-4-native-depository-only.txt

  Scenario: Constructor graph resolves critical services
    Tool: Bash
    Steps: Build x64 solution with Visual Studio MSBuild command.
    Expected: No compile errors from missing registrations/types.
    Evidence: .sisyphus/evidence/task-4-build.log
  ```

  **Commit**: YES | Message: `refactor(app): bootstrap native Depository container` | Files: [HyPlayer/App.xaml.cs, HyPlayer/App/AppDepository.cs or equivalent]

- [x] 5. Implement HyPlayItem Compatibility Adapters

  **What to do**: Add app-layer adapters that convert `ProvidableItemBase`/`SingleSongBase`/provider identity to existing `HyPlayItem`, and convert `HyPlayItem` back to provider identity for transitional paths. Include explicit comments marking adapters temporary and removal criteria: all UI/viewmodels use provider item view models directly and `PlaylistService` no longer stores `HyPlayItem` as its internal canonical model.
  **Must NOT do**: Do not put `HyPlayItem` references in PlayCore or NeteaseProvider.

  **Recommended Agent Profile**:
  - Category: `deep` - Reason: compatibility seam prevents big-bang rewrite.
  - Skills: [`dotnet-best-practices`] - mapping and nullability safety.
  - Omitted: [`simplify`] - behavior must be preserved before simplification.

  **Parallelization**: Can Parallel: YES | Wave 2 | Blocks: [7,8,9,10] | Blocked By: [2]

  **References**:
  - Pattern: `HyPlayer/Domain` - current domain models including `HyPlayItem`.
  - Pattern: `HyPlayer/Services/Playback/PlaylistService/PlaylistService.Netease.cs` - current NCSong→HyPlayItem mapping.
  - API/Type: `E:/HyPlayer/HyPlayer.PlayCore/HyPlayer.PlayCore.Abstraction/Models/SingleItems/SingleSongBase.cs`.

  **Acceptance Criteria**:
  - [ ] Adapter round-trip preserves provider id, type id, actual id, title, creators, album/container identity, cover identity, and duration when available.
  - [ ] PlayCore and NeteaseProvider projects do not reference `HyPlayItem`.
  - [ ] Adapter removal criteria are documented in comments and Task 14 evidence.

  **QA Scenarios**:
  ```
  Scenario: Adapter preserves song identity
    Tool: Bash
    Steps: Add/run targeted tests or compile-time checks for provider item -> HyPlayItem -> provider identity round-trip using sample ncm song id.
    Expected: Provider/type/actual ids remain unchanged.
    Evidence: .sisyphus/evidence/task-5-adapter-roundtrip.log

  Scenario: No HyPlayItem leak into libraries
    Tool: Bash
    Steps: Run grep for "HyPlayItem" under E:\HyPlayer\HyPlayer.PlayCore and E:\HyPlayer\HyPlayer.NeteaseProvider\HyPlayer.NeteaseProvider.
    Expected: No matches.
    Evidence: .sisyphus/evidence/task-5-no-hyplayitem-leak.txt
  ```

  **Commit**: YES | Message: `refactor(app): add provider item compatibility adapters` | Files: [HyPlayer/** adapter files, optional tests]

- [x] 6. Move Missing NetEase Operations Behind NeteaseProvider

  **What to do**: Implement NeteaseProvider-side support for operations not already covered by existing interfaces. Existing provider capabilities include `IMusicResourceProvidable`, `ILyricProvidable`, `IProvidableItemProvidable`, `IProvidableItemRangeProvidable`, `ISearchableProvider`, `IRecommendationProvidable`. Keep all direct `NeteaseApi` calls in NeteaseProvider. Model likes and playlist adds through provider item/container semantics; null target id = user favorite/heart, non-null target id = target playlist/container.
  **Must NOT do**: Do not expose `NeteaseApis.*` or Netease DTOs through public app-facing contracts.

  **Recommended Agent Profile**:
  - Category: `deep` - Reason: provider boundary implementation and tests.
  - Skills: [`dotnet-best-practices`, `csharp-tunit`] - provider tests use TUnit.
  - Omitted: [`testcontainers`] - no Docker dependencies.

  **Parallelization**: Can Parallel: YES | Wave 2 | Blocks: [7,8,10,12] | Blocked By: [1,2]

  **References**:
  - Pattern: `E:/HyPlayer/HyPlayer.NeteaseProvider/HyPlayer.NeteaseProvider/NeteaseProvider.cs` - provider implementation.
  - Test: `E:/HyPlayer/HyPlayer.NeteaseProvider/HyPlayer.NeteaseProvider.Tests/NeteaseApisTests.cs` - TUnit style.
  - API/Type: `E:/HyPlayer/HyPlayer.PlayCore/HyPlayer.PlayCore.Abstraction/Interfaces/Provider/IProvableItemLikable.cs`.

  **Acceptance Criteria**:
  - [ ] NeteaseProvider public API returns PlayCore abstractions, not Netease API DTOs.
  - [ ] Provider tests cover stream URL, lyric, search, item detail, range/container detail, like, and add-to-playlist/container where secrets allow.
  - [ ] HyPlayer app can consume each operation without referencing `HyPlayer.NeteaseApi`.

  **QA Scenarios**:
  ```
  Scenario: Provider encapsulates NetEase APIs
    Tool: Bash
    Steps: Run grep for "NeteaseApis|NeteaseCloudMusicApiHandler|HyPlayer.NeteaseApi" under HyPlayer/ after app migration and under NeteaseProvider public contracts.
    Expected: Matches allowed only inside NeteaseProvider internals/tests, not app or public abstraction leakage.
    Evidence: .sisyphus/evidence/task-6-provider-encapsulation.txt

  Scenario: Provider tests cover migrated operations
    Tool: Bash
    Steps: Run NeteaseProvider TUnit tests with configured secrets or compile-only fallback if secrets unavailable.
    Expected: Tests pass; if secrets unavailable, build passes and secret-dependent tests are documented as skipped/not run.
    Evidence: .sisyphus/evidence/task-6-neteaseprovider-tests.log
  ```

  **Commit**: YES | Message: `refactor(provider): expose NetEase operations through PlayCore abstractions` | Files: [E:/HyPlayer/HyPlayer.NeteaseProvider/**, E:/HyPlayer/HyPlayer.PlayCore/** if needed]

- [ ] 7. Replace HyPlayer NetEase Read Paths With Provider Queries

  **What to do**: Replace app direct reads for stream URL, lyrics, song/album/artist/playlist/radio detail, personal FM, recommendations, and search with NeteaseProvider/PlayCore abstraction calls. Use adapters from Task 5 only at UI boundaries. Prioritize high-risk files: playback media providers, queue providers, `LyricService`, `PersonalFmStrategy`, and `ShellSearchViewModel`.
  **Must NOT do**: Do not rewrite UI behavior or visual layout; do not keep fallback direct API calls.

  **Recommended Agent Profile**:
  - Category: `deep` - Reason: broad replacement across core read paths.
  - Skills: [`dotnet-best-practices`] - async/error handling and DI.
  - Omitted: [`frontend-design`] - no styling.

  **Parallelization**: Can Parallel: YES | Wave 3 | Blocks: [11,13,14] | Blocked By: [1,2,5,6]

  **References**:
  - Pattern: `HyPlayer/Services/Playback/MediaProviders/NeteaseStreamingProvider.cs`.
  - Pattern: `HyPlayer/Services/Playback/MediaProviders/CachedNeteaseProvider.cs`.
  - Pattern: `HyPlayer/Services/Playback/LyricService.cs`.
  - Pattern: `HyPlayer/Services/Playback/QueueProviders/*.cs`.
  - Pattern: `HyPlayer/Services/Playback/Strategies/PersonalFmStrategy.cs`.
  - Pattern: `HyPlayer/Shell/Search/ShellSearchViewModel.cs`.

  **Acceptance Criteria**:
  - [ ] All read-only NetEase app paths call provider abstractions.
  - [ ] User-facing error handling remains equivalent or better.
  - [ ] Direct API grep count decreases to zero for migrated read paths.

  **QA Scenarios**:
  ```
  Scenario: Search/read path compiles without direct API
    Tool: Bash
    Steps: Run grep for direct NetEase API tokens in migrated read files, then build x64 solution.
    Expected: No direct NetEase tokens in migrated read files; build succeeds.
    Evidence: .sisyphus/evidence/task-7-read-paths-build.log

  Scenario: Missing provider result fails gracefully
    Tool: Bash
    Steps: Use unit/diagnostic harness or app-level stub provider to return missing resource for one song/lyric request.
    Expected: Existing notification/error UI path is invoked; no crash.
    Evidence: .sisyphus/evidence/task-7-read-paths-error.log
  ```

  **Commit**: YES | Message: `refactor(app): route NetEase read paths through provider abstractions` | Files: [HyPlayer/Services/Playback/**, HyPlayer/Shell/Search/**]

- [ ] 8. Replace HyPlayer NetEase Mutation Paths With Container Operations

  **What to do**: Replace app mutations for like/unlike, add/remove playlist tracks, cloud delete, playlist create/delete/privacy, comment like/floor where retained, and personal FM trash with provider/container abstractions. Treat user liked songs and playlists as containers of `ProvidableItemBase`; use target container id for add/remove, null target for heart/favorite where the provider contract defines that behavior.
  **Must NOT do**: Do not let UI controls call `NeteaseApis.*` or Netease DTO request classes.

  **Recommended Agent Profile**:
  - Category: `deep` - Reason: user-data mutating paths need correctness.
  - Skills: [`dotnet-best-practices`] - async commands, error handling.
  - Omitted: [`csharp-tunit`] - app lacks test project; provider tests covered in Task 6/12.

  **Parallelization**: Can Parallel: YES | Wave 3 | Blocks: [11,13,14] | Blocked By: [1,5,6]

  **References**:
  - Pattern: `HyPlayer/UI/Lists/SongsList.xaml.cs` - playlist/cloud mutations.
  - Pattern: `HyPlayer/UI/Dialogs/SongListSelectDialog.xaml.cs` - add to playlist.
  - Pattern: `HyPlayer/UI/Dialogs/CreateSonglistDialog.xaml.cs` - create playlist.
  - Pattern: `HyPlayer/UI/Lists/PlaylistItem.xaml.cs` - delete/privacy playlist.
  - Pattern: `HyPlayer/UI/Controls/SingleComment.xaml.cs` - comment operations.
  - Pattern: `HyPlayer/UI/Playback/PlayBar/PlayBar.xaml.cs` - personal FM trash.

  **Acceptance Criteria**:
  - [ ] All mutation UI/service paths use provider abstraction services.
  - [ ] Operations have explicit success/failure notification behavior.
  - [ ] No app code constructs NetEase request DTOs.

  **QA Scenarios**:
  ```
  Scenario: Add song to playlist uses container abstraction
    Tool: Bash
    Steps: Run grep for "PlaylistTracksEditApi|PlaylistCreateApi|PlaylistDeleteApi|PlaylistPrivacyApi|CloudDeleteApi" under HyPlayer/.
    Expected: No app matches; provider-side implementation only.
    Evidence: .sisyphus/evidence/task-8-mutations-grep.txt

  Scenario: Mutation failure shows notification
    Tool: Bash
    Steps: Use stub/failing provider or existing error path to simulate add-to-container failure.
    Expected: User notification path is invoked; UI remains stable.
    Evidence: .sisyphus/evidence/task-8-mutations-error.log
  ```

  **Commit**: YES | Message: `refactor(app): model NetEase mutations as provider container operations` | Files: [HyPlayer/UI/**, HyPlayer/Services/**, provider abstraction files]

- [ ] 9. Migrate Playback Queue and Resource Ownership Toward PlayCore

  **What to do**: Move playlist/queue/resource semantics from HyPlayer services toward PlayCore managers/controllers where compatible. Keep UWP-specific `AudioGraphPlayer` or platform audio surface in HyPlayer only where PlayCore lacks UWP implementation compatibility. Register PlayCore controllers/managers/providers through Depository, using `IEnumerable<T>` for strategies/providers. Ensure current strategy IDs (`seq`, `sgl`, `shn`, `pfm`, `ltg`; transitions `dir`, `xfd`, `gap`) remain behaviorally compatible.
  **Must NOT do**: Do not break existing navigation/playbar commands; do not migrate platform-specific UWP APIs into provider-neutral PlayCore abstractions.

  **Recommended Agent Profile**:
  - Category: `ultrabrain` - Reason: hard playback boundary and behavior preservation.
  - Skills: [`dotnet-best-practices`] - architecture and async state.
  - Omitted: [`frontend-design`] - no visual changes.

  **Parallelization**: Can Parallel: YES | Wave 3 | Blocks: [11,13,14] | Blocked By: [1,2,5]

  **References**:
  - Pattern: `HyPlayer/Services/Playback/PlaylistService.cs` and partials.
  - Pattern: `HyPlayer/Services/Playback/PlaybackStateService.cs`.
  - Pattern: `HyPlayer/Services/Playback/PlaybackControlService.cs`.
  - API/Type: `E:/HyPlayer/HyPlayer.PlayCore/HyPlayer.PlayCore.Abstraction/PlayListManagerBase.cs`.
  - API/Type: `E:/HyPlayer/HyPlayer.PlayCore/HyPlayer.PlayCore.Abstraction/PlayControllerBase.cs`.
  - API/Type: `E:/HyPlayer/HyPlayer.PlayCore/HyPlayer.PlayCore.Abstraction/PlayCoreBase.cs`.

  **Acceptance Criteria**:
  - [ ] Provider/resource/playlist semantics live behind PlayCore-facing services.
  - [ ] UWP audio-specific code remains app-side with documented boundary if not migrated.
  - [ ] Strategy and transition IDs remain compatible.
  - [ ] Existing PlayBarViewModel state updates still receive playback messages/state.

  **QA Scenarios**:
  ```
  Scenario: Sequential playback path still resolves next item
    Tool: Bash
    Steps: Run targeted playback service tests/harness or compile-time diagnostic with seq strategy and two provider-backed songs.
    Expected: Next item resolution uses PlayCore/provider path and returns the second item.
    Evidence: .sisyphus/evidence/task-9-playback-seq.log

  Scenario: Platform boundary is documented
    Tool: Bash
    Steps: Search migrated playback code for direct UWP audio usage and confirm each occurrence is inside app-side platform layer, not PlayCore abstractions.
    Expected: No UWP audio references in PlayCore abstraction/provider-neutral projects.
    Evidence: .sisyphus/evidence/task-9-platform-boundary.txt
  ```

  **Commit**: YES | Message: `refactor(playback): move queue and resource semantics toward PlayCore` | Files: [HyPlayer/Services/Playback/**, E:/HyPlayer/HyPlayer.PlayCore/** if needed]

- [ ] 10. Replace Code-Behind and ViewModel Service Resolution With Depository

  **What to do**: Replace all `CommunityToolkit.Mvvm.DependencyInjection.Ioc.Default.GetRequiredService<T>()` usage in XAML code-behind, controls, pages, viewmodels, and services with the app's native Depository resolver or constructor-injected dependencies where feasible. High-impact files include `PlayBar.xaml.cs`, `LyricControl.xaml.cs`, `SongsList.xaml.cs`, dialogs, `MainPage.xaml.cs`, and shell/search/navigation files.
  **Must NOT do**: Do not introduce a generic static service locator beyond the minimum app-owned Depository access required by XAML construction limitations; prefer constructor injection where XAML does not block it.

  **Recommended Agent Profile**:
  - Category: `unspecified-high` - Reason: broad mechanical migration with XAML constraints.
  - Skills: [`dotnet-best-practices`] - DI and UI code-behind practices.
  - Omitted: [`frontend-design`] - no UI redesign.

  **Parallelization**: Can Parallel: YES | Wave 4 | Blocks: [11,13] | Blocked By: [4,5,6]

  **References**:
  - Pattern: `HyPlayer/UI/Playback/PlayBar/PlayBar.xaml.cs` - many service resolutions.
  - Pattern: `HyPlayer/UI/Playback/LyricControl/LyricControl.xaml.cs`.
  - Pattern: `HyPlayer/UI/Lists/SongsList.xaml.cs`.
  - API/Type: `E:/Personal/Depository/src/Depository.Extensions/ResolveExtension.cs`.

  **Acceptance Criteria**:
  - [ ] Grep finds no `CommunityToolkit.Mvvm.DependencyInjection` or `Ioc.Default` under `HyPlayer/`.
  - [ ] XAML-created controls still initialize viewmodels/services deterministically.
  - [ ] Resolver failures fail early with clear exception messages during startup/initialization.

  **QA Scenarios**:
  ```
  Scenario: No old Ioc usage remains
    Tool: Bash
    Steps: Run grep for "CommunityToolkit.Mvvm.DependencyInjection|Ioc.Default|GetRequiredService" under HyPlayer/.
    Expected: No old DI matches; any remaining GetRequiredService is platform API and documented.
    Evidence: .sisyphus/evidence/task-10-no-old-ioc.txt

  Scenario: Key XAML controls compile
    Tool: Bash
    Steps: Build x64 solution after DI code-behind migration.
    Expected: No XAML/code-behind compile errors.
    Evidence: .sisyphus/evidence/task-10-xaml-build.log
  ```

  **Commit**: YES | Message: `refactor(app): replace Ioc service resolution with Depository` | Files: [HyPlayer/UI/**, HyPlayer/Shell/**, HyPlayer/App/**]

- [ ] 11. Remove Direct NetEase App Dependencies and Clean Transitional References

  **What to do**: Remove direct `HyPlayer.NeteaseApi` usings/references from HyPlayer app project after Tasks 7-10. Remove obsolete app-side API handler registrations. Keep NeteaseProvider project references. Ensure app project no longer needs direct NeteaseApi types. Update `JsonDefaultContext.cs` if new app-level serialized types were introduced.
  **Must NOT do**: Do not remove NeteaseApi from NeteaseProvider internals/tests.

  **Recommended Agent Profile**:
  - Category: `unspecified-high` - Reason: cleanup with build verification.
  - Skills: [`dotnet-best-practices`, `dotnet-aot-compat`] - dependency and serialization safety.
  - Omitted: [`simplify`] - no cosmetic refactor.

  **Parallelization**: Can Parallel: YES | Wave 4 | Blocks: [13,14] | Blocked By: [4,7,8,9,10]

  **References**:
  - Project: `HyPlayer/HyPlayer.csproj`.
  - AOT: `HyPlayer/Classes/JsonDefaultContext.cs`.
  - CI placeholders: `HyPlayer/Classes/BuildInfo.cs`, `HyPlayer/Classes/LastFMConstants.cs` - do not put secrets.

  **Acceptance Criteria**:
  - [ ] `HyPlayer/HyPlayer.csproj` has no direct NeteaseApi project/package reference unless required transitively through NeteaseProvider only.
  - [ ] Grep finds no app production `using HyPlayer.NeteaseApi`.
  - [ ] New serialized app types are registered in `JsonDefaultContext.cs` if applicable.

  **QA Scenarios**:
  ```
  Scenario: Direct NetEase app dependency removed
    Tool: Bash
    Steps: Run grep for "HyPlayer.NeteaseApi|NeteaseApis|NeteaseCloudMusicApiHandler" under HyPlayer/ and inspect HyPlayer.csproj.
    Expected: No app production matches/references.
    Evidence: .sisyphus/evidence/task-11-no-direct-netease.txt

  Scenario: AOT serialization guard
    Tool: Bash
    Steps: Search for new System.Text.Json serialization calls and compare types against JsonDefaultContext.
    Expected: Any new app-level serialized types are source-generated or no new serialization added.
    Evidence: .sisyphus/evidence/task-11-aot-json.txt
  ```

  **Commit**: YES | Message: `refactor(app): remove direct NetEase API dependencies` | Files: [HyPlayer/HyPlayer.csproj, HyPlayer/**]

- [ ] 12. Add/Update Tests for Provider, PlayCore, and Depository Composition

  **What to do**: Add tests where supported: PlayCore tests for new abstraction behavior; NeteaseProvider TUnit tests for provider operations and container semantics; lightweight app composition verification if feasible without creating a full UWP test project. Ensure Depository multi-registration/`IEnumerable<T>` assumptions are covered by either direct local Depository tests reference or app-level composition tests.
  **Must NOT do**: Do not create a fragile UWP UI test project unless explicitly needed; do not require real NetEase secrets for non-secret tests.

  **Recommended Agent Profile**:
  - Category: `unspecified-high` - Reason: cross-project test strategy.
  - Skills: [`csharp-tunit`, `dotnet-best-practices`] - TUnit provider tests.
  - Omitted: [`testcontainers`] - no containers.

  **Parallelization**: Can Parallel: YES | Wave 4 | Blocks: [13] | Blocked By: [3,6]

  **References**:
  - Test: `HyPlayer.NeteaseProvider/HyPlayer.NeteaseProvider.Tests/NeteaseApisTests.cs` - TUnit patterns.
  - Test: `E:/Personal/Depository/Depository.Tests/DepositoryResolveTests.cs:333-347` - `Resolve<IEnumerable<T>>` proof.
  - Test: `E:/Personal/Depository/Depository.Tests/DepositoryResolveTests.cs:397-411` - constructor injection proof.

  **Acceptance Criteria**:
  - [ ] Provider tests cover migrated operations or explicitly skip secret-dependent runtime calls.
  - [ ] Composition test/harness verifies Depository resolves multi-provider/strategy `IEnumerable<T>`.
  - [ ] No test commits secrets.

  **QA Scenarios**:
  ```
  Scenario: TUnit provider tests run
    Tool: Bash
    Steps: Run NeteaseProvider test command under its repository setup with MTP/TUnit configuration.
    Expected: Tests pass or secret-dependent tests are clearly skipped/documented.
    Evidence: .sisyphus/evidence/task-12-provider-tests.log

  Scenario: Depository IEnumerable composition verified
    Tool: Bash
    Steps: Run composition test/harness resolving IEnumerable of providers/strategies.
    Expected: Multiple registered implementations resolve and constructor injection works.
    Evidence: .sisyphus/evidence/task-12-depository-enumerable.log
  ```

  **Commit**: YES | Message: `test(migration): cover provider and Depository composition paths` | Files: [E:/HyPlayer/HyPlayer.NeteaseProvider/**Tests**, E:/HyPlayer/HyPlayer.PlayCore/**Tests**, optional HyPlayer test harness]

- [ ] 13. Run Full Windows Build/Package Verification and Static Guardrails

  **What to do**: Run the repo-approved Windows build verification commands. Use Visual Studio MSBuild, not bare `dotnet build`. Capture restore, solution build, package build, and static grep guardrails into evidence files.
  **Must NOT do**: Do not trust an `AnyCPU` or bare `dotnet build` result as final verification.

  **Recommended Agent Profile**:
  - Category: `unspecified-high` - Reason: environment-specific build verification.
  - Skills: [`dotnet-best-practices`] - MSBuild verification discipline.
  - Omitted: [`csharp-tunit`] - tests already handled in Task 12.

  **Parallelization**: Can Parallel: NO | Wave 5 | Blocks: [14, Final] | Blocked By: [3,4,7,8,9,10,11,12]

  **References**:
  - Build: `AGENTS.md` verified commands.
  - Solution: `HyPlayer.slnx`.
  - Package: `HyPlayer.Package/HyPlayer.Package.wapproj`.

  **Acceptance Criteria**:
  - [ ] Restore command succeeds.
  - [ ] Whole-solution x64 build succeeds.
  - [ ] Package x64 build succeeds with signing disabled.
  - [ ] Static guardrails show no direct NetEase app calls and no old/MSDI DI usage.

  **QA Scenarios**:
  ```
  Scenario: Approved x64 restore/build/package succeeds
    Tool: Bash
    Steps: Run AGENTS.md restore, solution build, and package build commands with /p:Platform=x64 /p:RuntimeIdentifier=win-x64.
    Expected: All commands exit 0.
    Evidence: .sisyphus/evidence/task-13-build-package.log

  Scenario: Static migration guardrails pass
    Tool: Bash
    Steps: Run grep for forbidden NetEase and DI tokens under HyPlayer/.
    Expected: No production matches.
    Evidence: .sisyphus/evidence/task-13-static-guardrails.txt
  ```

  **Commit**: NO | Message: `n/a` | Files: [.sisyphus/evidence/task-13-*]

- [ ] 14. Document Adapter Exit Criteria and Final Migration Boundary

  **What to do**: Produce final migration boundary documentation in `.sisyphus/evidence/task-14-boundary.md` and, if appropriate, concise code comments near adapters. Document what remains app-owned (UWP audio surface, UI viewmodels/pages), what is provider-owned (NetEase API/session/data operations), what is PlayCore-owned (provider-neutral items/containers/resources/playback-content semantics), and when temporary `HyPlayItem` adapters can be removed.
  **Must NOT do**: Do not create docs outside `.sisyphus/evidence/` unless the user separately requests documentation changes.

  **Recommended Agent Profile**:
  - Category: `writing` - Reason: technical boundary documentation.
  - Skills: [] - no special skill needed.
  - Omitted: [`docx`] - no Word document.

  **Parallelization**: Can Parallel: YES | Wave 5 | Blocks: [Final] | Blocked By: [7,8,9,11,13]

  **References**:
  - Adapter: files added in Task 5.
  - Boundary: PlayCore abstractions and NeteaseProvider implementation files.
  - Metis guardrail: adapter layer must have exit/removal criteria.

  **Acceptance Criteria**:
  - [ ] Evidence doc states ownership boundaries and adapter exit criteria.
  - [ ] Evidence doc lists remaining intentional app-side responsibilities.
  - [ ] No new tracked documentation outside allowed evidence unless requested.

  **QA Scenarios**:
  ```
  Scenario: Boundary doc is complete
    Tool: Bash
    Steps: Verify task-14-boundary.md contains sections for HyPlayer, PlayCore, NeteaseProvider, Depository, and Adapter Exit Criteria.
    Expected: All required sections present.
    Evidence: .sisyphus/evidence/task-14-boundary.md

  Scenario: No accidental docs mutation
    Tool: Bash
    Steps: Run git diff --name-only and check documentation changes.
    Expected: Only intended code changes plus .sisyphus/evidence files; no docs/README mutation unless explicitly approved.
    Evidence: .sisyphus/evidence/task-14-diff-files.txt
  ```

  **Commit**: NO | Message: `n/a` | Files: [.sisyphus/evidence/task-14-boundary.md]

## Final Verification Wave (MANDATORY — after ALL implementation tasks)
> 4 review agents run in PARALLEL. ALL must APPROVE. Present consolidated results to user and get explicit "okay" before completing.
> **Do NOT auto-proceed after verification. Wait for user's explicit approval before marking work complete.**
> **Never mark F1-F4 as checked before getting user's okay.** Rejection or user feedback -> fix -> re-run -> present again -> wait for okay.
- [ ] F1. Plan Compliance Audit — oracle
- [ ] F2. Code Quality Review — unspecified-high
- [ ] F3. Real Manual QA — unspecified-high (+ app launch/manual UI path if feasible on Windows)
- [ ] F4. Scope Fidelity Check — deep

## Commit Strategy
- Use small commits per task when `Commit: YES` is specified.
- Never commit secrets or CI-mutated placeholder values from `BuildInfo.cs` / `LastFMConstants.cs`.
- Recommended sequence:
  1. `build(app): reference native Depository dependencies`
  2. `refactor(playcore): add provider content boundary contracts`
  3. `refactor(provider): expose NetEase operations through PlayCore abstractions`
  4. `refactor(app): bootstrap native Depository container`
  5. `refactor(app): add provider item compatibility adapters`
  6. `refactor(app): route NetEase read paths through provider abstractions`
  7. `refactor(app): model NetEase mutations as provider container operations`
  8. `refactor(playback): move queue and resource semantics toward PlayCore`
  9. `refactor(app): replace Ioc service resolution with Depository`
  10. `refactor(app): remove direct NetEase API dependencies`
  11. `test(migration): cover provider and Depository composition paths`

## Success Criteria
- HyPlayer app has no direct `HyPlayer.NeteaseApi`, `NeteaseApis`, or `NeteaseCloudMusicApiHandler` production references.
- HyPlayer app has no `CommunityToolkit.Mvvm.DependencyInjection.Ioc`, `ServiceCollection`, `BuildServiceProvider`, or `Depository.Extensions.DependencyInjection` production usage.
- NeteaseProvider is the only NetEase API boundary and exposes PlayCore/provider abstractions to the app.
- PlayCore remains provider-neutral and UWP-neutral.
- Native Depository multi-registration and `IEnumerable<T>` resolution are used for strategies/providers where applicable.
- Approved Windows restore/build/package commands pass for x64.
- Final verification agents approve and user explicitly says okay.
