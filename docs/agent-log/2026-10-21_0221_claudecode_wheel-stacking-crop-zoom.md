---
date: "2026-10-21 02:21:14"
agent: "claudecode"
model: "Claude Opus 5.5"
session_type: "bugfix"
status: "completed"
sprint: "Linux port"
related_files:
  - "src/OgConverter.Linux/Views/WheelOverlay.cs"
  - "src/OgConverter.Linux/Services/DBusService.cs"
  - "linux/gnome-extension/og-converter@dr-at0mic.github.io/extension.js"
  - "src/OgConverter.Windows/Controls/CropCanvas.cs"
  - "src/OgConverter.Linux/Controls/CropCanvas.cs"
---

# Agent Session Log: Linux wheel hidden or empty on open, crop dialog zoom

## 1. Context & Objectives
- **User Goal**: (1) On Linux the drag-and-tap wheel opens empty until the mouse moves; (2) sometimes only a "“OG Converter Wheel <pid>” is ready" notification and the open sound appear, with no wheel; (3) add zoom to the crop dialog, because a README rendered to one long PNG (980 × 16696) is a thin strip.
- **User reports**: Linux sounds work; Windows sounds work again after a restart; Windows PDF printing still fails at random now and then ("sort it out another time").
- **Starting State**: Previous log `2026-10-21_0139_claudecode_browser-pdf-race.md`, its changes still uncommitted.

## 2. Actions Completed
- [x] Diagnosed (2) on the VM with xprop: while open, the wheel had an empty `_NET_WM_STATE` (no ABOVE, no SKIP_TASKBAR) and `_NET_WM_USER_TIME = 0`. Mutter unmanages a window when it's hidden, so every open is a "first map"; with user time 0 its focus-stealing prevention stacks the wheel just below the focused window (Files) and, when they overlap, sets demands-attention, which GNOME Shell turns into the "is ready" notification (skipped for skip-taskbar windows). Avalonia doesn't re-send Topmost/ShowInTaskbar after a hide.
- [x] Fix in the app: `WheelOverlay.ReassertWindowState()` toggles Topmost and ShowInTaskbar before each `Show()`. Verified on the VM: 3 opens in a row all carry `_NET_WM_STATE_SKIP_TASKBAR, _NET_WM_STATE_ABOVE`.
- [x] Fix in the extension (version 2): `MoveWindowByTitle` also `make_above()`, `raise()` and `unset_demands_attention()`.
- [x] Diagnosed (1) with an x11grab capture: the wheel renders, but in the "no file yet" state (ring and file icon, no options). The dragged file only reaches the window on the next drag motion, and the wheel opens under a still pointer.
- [x] Fix: new extension method `NudgePointer()` (virtual pointer, +1 px, back after 30 ms); `WheelOverlay.PlaceAndRevealAsync` calls it in drag mode after placing the wheel, when no file has arrived yet. `DBusService.NudgePointerAsync` returns false on older extensions (ignored).
- [x] Crop zoom on Windows and Linux: Ctrl+scroll zooms at the pointer (up to 8 screen px per image px), scroll / Shift+scroll / middle-drag / Space+drag pan, auto-scroll while dragging a handle past the view's edge, buttons − / % / + / Fit / Width, Ctrl+= / Ctrl+− / Ctrl+0. "Width" fills the width at the top of the selection. At zoom 1 the layout is exactly the old one.
- [x] Linux `DrawChecker` now draws only the visible part of the picture (zoomed in, the full rect would be millions of squares).
- [x] `--snapshot` on both platforms gains `dialog_crop_long.png` (980 × 6000 test picture, zoomed to width).

## 3. Files Created / Modified / Deleted
| Action | File Path | Description |
|---|---|---|
| `MODIFIED` | `src/OgConverter.Linux/Views/WheelOverlay.cs` | `ReassertWindowState()` before `Show()`; nudge after placing in drag mode. |
| `MODIFIED` | `src/OgConverter.Linux/Services/DBusService.cs` | `NudgePointerAsync()`. |
| `MODIFIED` | `linux/gnome-extension/og-converter@dr-at0mic.github.io/extension.js` | API version 2: `NudgePointer`, raise/above/unset-attention in `MoveWindowByTitle`, cleanup in `disable()`. |
| `MODIFIED` | `linux/gnome-extension/og-converter@dr-at0mic.github.io/metadata.json` | `version` 2. |
| `MODIFIED` | `linux/gnome-extension/README.md` | Documents the new method and version. |
| `MODIFIED` | `src/OgConverter.Windows/Controls/CropCanvas.cs`, `src/OgConverter.Linux/Controls/CropCanvas.cs` | Zoom, pan, auto-scroll, `ViewChanged`. |
| `MODIFIED` | `src/OgConverter.Windows/Views/CropDialog.xaml(.cs)`, `src/OgConverter.Linux/Views/CropDialog.axaml(.cs)` | Zoom buttons and label, shortcuts, hint text. |
| `MODIFIED` | `src/OgConverter.Windows/Tests/SnapshotRunner.cs`, `src/OgConverter.Linux/Tests/SnapshotRunner.cs` | `dialog_crop_long` snapshot. |

## 4. Key Decisions & Rationales
- **Re-assert window state in the app, and also raise from the extension**: the app fix alone stops the stacking and the notification; the extension change covers anything else that stacks it under.
- **Pointer nudge instead of guessing the file**: Meta.Dnd doesn't expose the drag's data, so the only way for the wheel to learn the file is a drag motion over it.
- **Zoom is relative to "fit"**: zoom 1 keeps the old centred layout, so the crop dialog looks and behaves as before until the user zooms (Windows behaviour unchanged by default).

## 5. Verification & Testing
- **Build**: `dotnet build og-converter.sln -c Release`: 0 warnings, 0 errors.
- **Windows self-test**: exit 0, 285 lines, identical to the pre-change baseline apart from timings and memory.
- **Snapshots**: Windows and Linux `dialog_crop_image` (zoom 1, unchanged layout plus the zoom controls) and `dialog_crop_long` (89%, top of the page) reviewed.
- **VM**: window states verified with xprop as above. The installed app was stopped for the test and restarted (`~/.local/share/og-converter/app/og-converter --background`, old build).
- **Not verified**: `NudgePointer` and the extension's raise. GNOME loads extension changes only at the next login, so they need the user to update the extension and log in again. Zoom interactions (Ctrl+scroll, panning, auto-scroll) were not exercised by hand; only the zoom-to-width state was rendered.

## 6. Blockers & Open Questions
- User to install the new build and extension on the VM (Settings › Desktop integration, then log out and in) and retest drag-and-tap.
- Still open from earlier: 7z wrong-password check is flaky; concurrent jobs share an output name; Windows "browser didn't produce a PDF" still happens at random (the error now carries the browser's exit code and last error line).

## 7. Next Agent Handoff (Action Items)
1. **Next Task**: Get the user's retest results for the wheel; ask for the new PDF error text when it recurs.
2. **Commands to Run**: VM wheel checks: see the `gdbus ... Trigger.KeyTapped` and `xprop` notes in memory/agent logs; `og-converter --snapshot <dir>` for the crop dialog.
3. **Watch Out For**: Extension API is now version 2; keep the README table in sync.
