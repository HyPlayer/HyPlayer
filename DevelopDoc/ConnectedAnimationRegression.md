# Lyrics collapse / DWM regression

The observed DWM failure was `0x8000FFFF` in
`dwmcore!CVisual::GetWorldTransform`, called from
`CCachedVisualImage::Snapshot` / `CComposition::PerformQueuedRenderSnapshots`.
Disabling the lyrics expand/collapse animation stopped the reproduction.

The fix retains Connected Animation. MainPage owns the collapse batch and keeps
the source page attached until every started animation completes. Failed starts,
interruption and the completion timeout explicitly cancel pending animations
before the source can be unloaded. A transition version prevents an old completion
from removing a reopened page.

## On-device verification

Use the newly built app, not an older installed package. Enable
**展开/收缩「歌词页面」动画** and record the test start time.

1. Play the same track/background that reproduced the failure. Expand and collapse
   the lyrics page at least ten times. Verify the cover/title connected animations
   still play, the compact page appears, and audio continues.
2. Repeat with Escape and the cover drag gesture, in both windowed and full-screen
   modes. Verify subsequent expansion has no residual drag offset.
3. Collapse and immediately expand again. Verify the reopened page remains visible
   after the old animation would have completed, and can still be closed normally.
4. Minimize during collapse, then restore. Verify no retained page or blocked input.
5. Repeat with a narrow/short window where compact animation targets are hidden,
   and with the animation setting disabled. Collapse must still complete.
6. Check Application event 1000 after the recorded start time for `dwm.exe` or
   `HyPlayer.exe`. No new matching crash, loss of taskbar input, or desktop restart
   is allowed. A build alone does not verify this graphics/compositor regression.

Example read-only event check (substitute the recorded local start time):

```powershell
Get-WinEvent -FilterHashtable @{
    LogName = 'Application'
    Id = 1000
    StartTime = [datetime]'YYYY-MM-DD HH:mm:ss'
} -ErrorAction SilentlyContinue |
    Where-Object Message -Match '(dwm|HyPlayer)\.exe' |
    Select-Object TimeCreated, Message
```
