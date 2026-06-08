# Decisions — HyPlayer PlayCore / NeteaseProvider / Depository Migration

## Architectural Decisions
1. **Adapter-first migration**: Keep `HyPlayItem` temporarily; add compatibility adapters; remove when UI/viewmodels use provider items directly.
2. **Auth/session boundary**: NetEase session lives in NeteaseProvider/provider context; HyPlayer stores app preferences only.
3. **Playback boundary**: UWP audio surface stays app-side if PlayCore lacks UWP implementation; provider/content/playlist semantics move to PlayCore.
4. **DI: Native Depository only**: No MSDI bridge, no `ServiceCollection`, no `Ioc.Default`.
5. **Multi-provider/strategy**: Use Depository `IEnumerable<T>` / `ResolveMultiple<T>`.

## Open Decisions
- Whether to move app-side logic into PlayCore or NeteaseProvider — defer to Task 1 inventory findings.
- Whether comments feature stays fully provider-facing or is removed — defer to Task 1.

## Task 2 PlayCore Provider Contracts - 2026-05-23
- Added provider-neutral capability contracts in PlayCore Abstraction rather than endpoint-specific NetEase mirrors. The contracts group Task 1 gaps by feature surface: auth/session, QR auth, containers, comments, suggestions, scoped paging, FM/recommendations, cloud/user library, rich media, and listen-together.
- Preserved `IProvableItemLikable` behavior by documenting `targetId == null` as heart/favorite and non-null `targetId` as add/remove from a target container.
- Added minimal neutral models (`ProviderSessionInfo`, `ProviderPageResult<T>`, QR login state, categories, dynamic metadata, rich media/cloud item bases) under `Models` / `Models\Containers` so contracts do not reference NetEase, UWP, or `HyPlayItem`.
- Added `ProviderContractTests` as compile-time and behavioral contract checks using test doubles only; no provider implementation was changed.

## Task 5 HyPlayItem Compatibility Adapter - 2026-05-23
- Added the temporary adapter in `HyPlayer/Domain/Music` beside the legacy model, rather than PlayCore or NeteaseProvider, to keep provider abstractions free of app UI types.
- Preserved provider/type/actual identity for adapted items with app-side weak metadata because `HyPlayItem` only has legacy `Id` plus computed playback `ProviderId`; this avoids changing the legacy model while still supporting round-trip provider identity.
- Adapter removal criterion remains: remove when all UI/viewmodels use provider item abstractions directly and PlaylistService no longer stores HyPlayItem as its internal canonical model.
