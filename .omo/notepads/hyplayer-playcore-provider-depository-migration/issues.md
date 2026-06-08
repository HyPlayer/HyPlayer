# Issues — HyPlayer PlayCore / NeteaseProvider / Depository Migration

## Known Risks
- 48 files with direct NetEaseApi usage — high migration surface.
- No main app test project — verification relies on build + grep + manual QA.
- UWP/XAML code-behind uses `Ioc.Default.GetRequiredService<T>()` — must find XAML-safe Depository access.
- AOT/source-gen constraints may limit Depository usage patterns.

## Blockers
None at start.
