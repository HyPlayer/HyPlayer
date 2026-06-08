# Learnings — HyPlayer PlayCore / NeteaseProvider / Depository Migration

## Conventions
- Native Depository only: no MSDI compatibility bridge.
- Only `NeteaseProvider` may reference/call `NeteaseApi`.
- PlayCore abstractions stay provider-neutral and UWP-neutral.
- AOT/source-gen constraints apply; use `JsonDefaultContext.cs` for new app serialized types.
- Verification uses VS MSBuild x64 commands, not bare `dotnet build`.

## Patterns Discovered
- Direct NetEaseApi usage in ~48 files across UI, services, providers, and dialogs.
- DI currently uses `CommunityToolkit.Mvvm.DependencyInjection.Ioc.Default.GetRequiredService<T>()`.
- Playback composed of `AudioGraphPlayer`, `PlaybackStateService`, `IPlaylistService`, `IPlaybackControlService`, `MediaSourceService`.
- PlayCore abstractions: `ProvidableItemBase`, `ContainerBase`, `SingleSongBase`, `LinerContainerBase`, `UndeterminedContainerBase`.
- `IProvableItemLikable` models heart (null target) and add-to-container (non-null target).
- Depository `IEnumerable<T>` works via `ResolveDependency` detecting `typeof(IEnumerable<>)` — returns typed array.
- Depository scopes via `CreateScope()` / `DepositoryResolveScope`.

## Task 1 Operation Inventory - 2026-05-23
- Direct non-generated NetEase API grep inventory is 48 files, 300 matches, and 96 NeteaseApis.* endpoint operations.
- Existing NeteaseProvider already implements ILyricProvidable, IMusicResourceProvidable, IProvableItemLikable, IProvidableItemProvidable, IProvidableItemRangeProvidable, ISearchableProvider, and IRecommendationProvidable; prefer these over new app-side NetEase wrappers.
- Operations needing new contracts include provider auth/session, playlist create/delete/privacy, comments, search suggestions, cloud library/upload, listen-together, video/mlog media/details, Personal FM feedback/trash, and richer paged container/category APIs.
- Playback migration inventory must include PlaybackStateService, PlaylistService, PlaybackControlService, MediaSourceService, LyricService, queue providers, media providers, play strategies, transitions, widgets, and shell playback surfaces.
- NetEase session state remains a provider-context responsibility; HyPlayer should stop owning cookies/login status directly except for provider-neutral app preferences.

## Task 3 — Add Native Depository 4.0.1 References (2026-05-23)
- Added Depository metapackage (4.0.1) to HyPlayer/HyPlayer.csproj via NuGet.
- Metapackage pulls in Depository.Abstraction, Depository.Core, Depository.Extensions.
- NO Depository.Extensions.DependencyInjection reference added (verified by grep and project.assets.json).
- Restore succeeded: msbuild HyPlayer.slnx /t:Restore /p:Configuration=Release /p:RuntimeIdentifier=win-x64 /p:Platform=x64 (exit code 0).
- Evidence saved to .sisyphus/evidence/task-3-restore.log and .sisyphus/evidence/task-3-no-depository-msdi-bridge.txt.
- CommunityToolkit.Mvvm left in place for Task 4 (DI bootstrap migration).

## Task 4 — Native Depository Bootstrap (2026-05-23T04:30:02.0096615+08:00)
- Replaced App.xaml.cs composition root with AppDepository.Initialize() and native Depository registrations only; no ServiceCollection, BuildServiceProvider, Ioc.Default.ConfigureServices, Microsoft.Extensions.DependencyInjection, or Depository.Extensions.DependencyInjection remains in production grep scope used for evidence.
- Added HyPlayer/App/AppDepository.cs as the app-owned static Depository root with Resolve<T>() and ResolveMultiple<T>() helpers for XAML/code-behind migration.
- Preserved existing lifetimes: singleton app/playback services and transient view models; multi-provider/strategy registrations remain multiple native Depository registrations so constructor IEnumerable<T> dependencies resolve through Depository.
- Replaced PlaybackControlService's MSDI IServiceProvider dependency with lazy AppDepository.Resolve<IPlaylistService>() to keep playback bootstrap native and avoid circular constructor injection.
- Verification: changed-file LSP diagnostics clean; x64 VS MSBuild solution/package build succeeded with exit code 0; build log notes existing Depository IL2104 trim warnings from package assemblies.

## Task 6 NetEaseProvider Contract Implementation - 2026-05-23
- `NeteaseProvider.ProviderContracts.cs` already contained method bodies for many Task 2 contracts, but `NeteaseProvider` only advertised older interfaces; explicitly declaring implemented PlayCore provider contracts is required for app/test capability discovery.
- Priority contracts now declared on `NeteaseProvider`: auth/session, container management, search suggestions, container paging, and Personal FM. Existing methods also justify declaring QR auth, comments, container categories, user library, context recommendations, scoped paging, and dynamic metadata.
- Playlist progressive paging had a root-cause bug: `NeteasePlaylist.GetProgressiveItemsListAsync` called `UpdatePlaylistInfoAsync` when `_trackIds` was null, but track IDs are populated by `UpdateTrackListAsync`.
- With placeholder `Secrets.cs`, `dotnet test` hits the known .NET 10 Microsoft.Testing.Platform VSTest-mode error; `dotnet run --project HyPlayer.NeteaseProvider.Tests.csproj` is the viable local runner, but many existing tests still fail live with 301 not-logged-in without real NetEase session secrets.
