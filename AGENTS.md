# AGENTS.md

## Build And Verification

- This is a Windows-only UWP/MSIX app targeting `net10.0-windows10.0.26100.0`; use Windows + Visual Studio/MSBuild, not a generic cross-platform .NET workflow.
- Clone/update with submodules. `HyPlayer.slnx` references `HyPlayer.Frieren`, `HyPlayer.NeteaseProvider`, `HyPlayer.UWP.Chopin`, `Impressionist`, `Kawazu`, and `Microsoft.Gaming.XboxGameBar.Projection`.
- Do not run a bare `dotnet build` and trust the result: default `AnyCPU` can make `Microsoft.Gaming.XboxGameBar.Projection` look for `runtimes\win10-AnyCPU\native\Microsoft.Gaming.XboxGameBar.dll`. Always pass a real platform/RID.
- CI restore shape for x64: `msbuild HyPlayer.slnx /t:Restore /p:Configuration=Release /p:RuntimeIdentifier=win-x64 /p:Platform=x64`.
- CI package build shape for x64: `msbuild HyPlayer.Package\HyPlayer.Package.wapproj /p:Configuration=Release /p:UapAppxPackageBuildMode=SideloadOnly /p:AppxBundle=Never /p:Platform=x64 /p:RuntimeIdentifier=win-x64 /p:AppxPackageSigningEnabled=false`.
- For arm64, use the same commands with `/p:Platform=arm64 /p:RuntimeIdentifier=win-arm64`.
- `nuget.config` clears inherited package sources; restore must use this repo config because CommunityToolkit Labs packages come from the Azure DevOps feed declared there.
- The main app has no dedicated test project. The only discovered test project is `HyPlayer.NeteaseProvider\HyPlayer.NeteaseProvider.Tests`, using TUnit/Microsoft.Testing.Platform; with .NET 10 SDK, plain `dotnet test` may fail unless MTP mode is opted in.

## Project Boundaries

- Main app code is under `HyPlayer/`; the MSIX packaging project is `HyPlayer.Package/HyPlayer.Package.wapproj` and points at `..\HyPlayer\HyPlayer.csproj` as the entry point.
- App composition starts in `HyPlayer/App.xaml.cs`: `InitializeServices` builds the `CommunityToolkit.Mvvm.DependencyInjection.Ioc` service provider and registers playback services, media providers, strategies, transitions, notification handlers, and view models.
- Playback state is centralized in `Services/Playback/PlaybackStateService.cs`; UI view models mirror state through `WeakReferenceMessenger` messages from `Services/Playback/Messages/PlaybackMessages.cs`.
- Media source routing is `HyPlayItem.ProviderId` -> `IMediaSourceProvider` in `Services/Playback/MediaProviders/MediaSourceService.cs`. Provider IDs are `ncm`, `lcl`, `nca`, and `nst`.
- Playlist behavior is strategy-based in `Services/Playback/PlaylistService.cs`; play strategies are registered with IDs such as `seq`, `sgl`, `shn`, `pfm`, and `ltg`, and transition strategies use IDs such as `dir`, `xfd`, and `gap`.

## AOT And Generated-Code Constraints

- The main app enables AOT-related settings (`PublishAot`, `LangVersion=preview`, `NoWarn=IL2026;IL3050`). Avoid reflection-heavy or dynamic-serialization changes unless they are explicitly source-generation safe.
- JSON serialization is source-generated in `HyPlayer/Classes/JsonDefaultContext.cs`; add new app-level serialized types there when using `System.Text.Json` in AOT-sensitive paths.
- `HyPlayer.NeteaseProvider/HyPlayer.NeteaseApi` uses T4/source-generated JSON context files (`NeteaseJsonSerializeContext.tt` and generated `.g.g.cs`). Do not hand-edit generated output unless you have verified the generation path.
- `BuildInfo.cs` and `LastFMConstants.cs` contain placeholders that CI replaces before packaging. Do not commit real Last.fm secrets or local build metadata into these files.

## Workflow Notes

- GitHub Actions build on `windows-2025-vs2026`, install .NET `10.0.x`, set up MSBuild, restore `HyPlayer.slnx`, mutate `HyPlayer.Package/Package.appxmanifest`, patch `BuildInfo.cs` and `LastFMConstants.cs`, then build `HyPlayer.Package.wapproj`.
- Release/Canary workflows produce unsigned MSIX packages locally first (`/p:AppxPackageSigningEnabled=false`) and submit signing separately. Do not assume local packaging needs the CI signing secrets.
- `README.md` explicitly warns the project is historically not clean MVVM; expect mixed code-behind and DI/view-model patterns rather than a uniform architecture.
- `.editorconfig` only disables a ReSharper unreachable-code heuristic for C#; there is no repo-wide formatter/linter contract beyond existing style.
