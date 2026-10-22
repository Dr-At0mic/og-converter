---
date: "2026-10-22 22:36:00"
agent: "claudecode"
model: "Claude Opus 5.5"
session_type: "bugfix"
status: "done"
sprint: "1.6.0"
related_files:
  - "src/OgConverter.Windows/Services/GlobalHookService.cs"
  - "src/OgConverter.Windows/Views/DragProbeWindow.cs"
  - "src/OgConverter.Windows/MainWindow.xaml.cs"
  - "src/OgConverter.Windows/Views/ExtrasDialog.xaml.cs"
  - "src/OgConverter.Windows/Platform/Windows/WingetInstaller.cs"
---

# Agent Session Log: wheel only on file drags, cancellable first-run install

## 1. Context & Objectives
- **User Goal**:
  1. Holding left click in a game (or any app) and pressing Shift opened the wheel. Restrict it to drags of files and folders.
  2. First-run "Recommended extras" install (LibreOffice/FFmpeg): the user couldn't close the app and had to use Task Manager.
- **Starting State**: `main` at `dd9b78d`, clean.

## 2. Actions Completed
- [x] Analysis first (user asked for no changes), then implemented on request.
- [x] Issue 1, three checks before the wheel opens from the hook:
  - Hook thread: records where the left button went down; a wheel key only counts once the cursor has left the system drag rectangle (`SM_CXDRAG`/`SM_CYDRAG`) around that point, the same test Explorer uses before starting a drag.
  - UI: no wheel while a full-screen app is in front (`SHQueryUserNotificationState` = busy or D3D full screen).
  - UI: new `DragProbeWindow`, a 48 px alpha-1 topmost, non-activating drop target put under the cursor for up to 600 ms (it follows the cursor and is cancelled on button up, Esc, or the key's release in hold-to-use mode). The wheel opens only when Windows offers it a drag whose data `DroppedFiles.GetPaths` understands (Explorer, VS Code, JetBrains…), and it opens with that path, so the Explorer-selection lookup is skipped for hook-opened wheels.
  - Already-open wheel: key taps still switch wheels immediately (no probe).
- [x] Issue 2: no restart is needed (FFmpeg is found in `WinGet\Links`, LibreOffice in Program Files, both re-detected right after install). The real problem was that the extras dialog had no way out while installing: no title bar, Cancel disabled, `Closing` cancelled, no timeout on winget, and the LibreOffice admin prompt often stays behind other windows because winget has no window. Now:
  - `IPackageInstaller.InstallAsync` / `DependencyService.InstallAsync` take an optional `CancellationToken`; `WingetInstaller` kills winget's process tree on cancel.
  - Extras dialog: "Not now" becomes "Cancel install" while installing; Esc and Alt+F4 also cancel. Remaining items show "Skipped". The footer notes that an installer already running as admin may still finish.
  - LibreOffice status text says to click the flashing shield on the taskbar if the admin prompt isn't visible.
  - After cancel, the helper is re-detected (it may be usable anyway).

## 3. Files Created / Modified / Deleted
| Action | File Path | Description |
|---|---|---|
| Created | `src/OgConverter.Windows/Views/DragProbeWindow.cs` | Drop-target probe + full-screen check |
| Modified | `src/OgConverter.Windows/Services/GlobalHookService.cs` | Button-down point, drag-threshold gate |
| Modified | `src/OgConverter.Windows/MainWindow.xaml.cs` | Probe wiring in `SetupGlobalHooks`, close on exit |
| Modified | `src/OgConverter.Core/Platform/IPackageInstaller.cs` | Optional `CancellationToken` |
| Modified | `src/OgConverter.Core/Services/DependencyService.cs` | Passes the token through |
| Modified | `src/OgConverter.Windows/Platform/Windows/WingetInstaller.cs` | Kill winget tree on cancel |
| Modified | `src/OgConverter.Linux/Platform/Linux/LinuxPackageInstaller.cs` | Signature only |
| Modified | `src/OgConverter.Windows/MainWindow.Dependencies.cs` | Cancel handling in `InstallDependencyAsync` |
| Modified | `src/OgConverter.Windows/Views/ExtrasDialog.xaml.cs` | Cancel install button / Esc / Alt+F4, taskbar hint |
| Modified | `src/OgConverter.Windows/Tests/SnapshotRunner.cs` | New install delegate signature |

## 4. Key Decisions
- Probe over an app allow-list: it checks the actual drag data, so it matches "files and folders only" exactly and needs no list of file-source apps. The wheel already relied on the drag loop reaching a window that appears under the cursor (its own `DragEnter`), so the probe uses the same mechanism.
- The probe's background is alpha 1, not 0: layered windows' fully transparent pixels are skipped by hit testing, so the drag would never reach it.
- No "Restart now" button: nothing needs a restart. The existing "Restart OG Converter to pick it up" message stays for the rare case detection misses it.
- No hard timeout on winget: LibreOffice can legitimately take a long time on slow connections; the cancel button is the way out.
- Settings › Engine install buttons left as they are: that path isn't modal, the app stays usable.

## 5. Verification
- `dotnet build og-converter.sln -c Release`: succeeded (Linux project compiled on Windows only to check the signature change).
- `og-converter.exe --test`: ALL VERIFICATION TESTS PASSED (only the two "must fail" OGX tamper checks report Success=False, as expected). Conversion code untouched.
- **Not tested by hand** (needs the user): Explorer / VS Code file drag + Shift opens the wheel; holding the button in a game or selecting text + Shift does not; the extras dialog's Cancel install on a PC without FFmpeg/LibreOffice. Linux not run on the VM (no behaviour change there).

## 6. Handover
- Released as **v1.6.1** at the user's request: `CHANGELOG.md` `## [1.6.1]` section added, committed on `main` as Dr_Atomic (no Claude attribution), tag `v1.6.1` pushed to trigger `release.yml`.
- If a real file drag ever fails to open the wheel when the cursor is perfectly still, raise `DragProbeWindow.Timeout` or check that the drag loop re-hit-tests on key changes.
