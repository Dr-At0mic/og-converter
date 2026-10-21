---
date: "2026-10-21 12:00:00"
agent: "claudecode"
model: "Claude Opus 5.5"
session_type: "analysis"
status: "completed"
sprint: "Linux port"
related_files:
  - "linux/gnome-extension/og-converter@dr-at0mic.github.io/extension.js"
  - "src/OgConverter.Linux/Views/WheelOverlay.cs"
  - "src/OgConverter.Linux/MainWindow.axaml"
  - "src/OgConverter.Linux/MainWindow.axaml.cs"
---

# Agent Session Log: Empty wheel on click+key, custom title bar on Linux (analysis only)

## 1. Context & Objectives
- **User Goal**: (1) Root cause: on Linux, pressing (not dragging) a file and tapping the key opens an empty wheel; slices appear once the mouse moves. (2) Feasibility of replacing GNOME's title bar with the app's own, as on Windows.
- **Starting State**: `main` at `10635da` (v1.5.0), clean.

## 2. Actions Completed
- [x] Traced issue 1 through `extension.js` (`DragTapDetector._tick`) and `WheelOverlay` (`ShowAt`, `PlaceAndRevealAsync`, `OnDragEnter`).
- [x] Traced issue 2 through `MainWindow.axaml` (`ExtendClientAreaToDecorationsHint`), `SetupTitleBar()`, `DialogWindow`.
- [ ] No code changed.

## 3. Files Created / Modified / Deleted
| Action | File Path | Description |
|---|---|---|
| `CREATED` | `docs/agent-log/2026-10-21_1200_claudecode_linux-empty-wheel-titlebar-analysis.md` | This log. |

## 4. Key Decisions & Rationales
- **Issue 1 root cause**: the extension fires `KeyTapped` on "button 1 held + modifier edge", not on a real drag. Before the pointer passes the toolkit's drag threshold (GTK default 8 px) Nautilus has not started a drag, so no drag data exists and the wheel opens in drag mode with no file (`ShowNoFile`, `GetWheelItems(null)` returns no items). `NudgePointer` moves 1 px, below the threshold, so it can't start the drag. Once the user moves, Nautilus starts the drag, the pointer is over the wheel, `OnDragEnter` gets the path and the slices appear.
- **Recommended fix for issue 1**: in the extension, when a key is tapped while `Meta.Dnd` has not reported `dnd-enter`, keep it pending and send `KeyTapped` on `dnd-enter` (if button 1 is still held); keep today's behaviour when `Meta.Dnd` is unavailable. Rejected: a bigger nudge (would start rubber-band selections / drags the user didn't ask for, threshold is configurable).
- **Issue 2 root cause**: `UsePlatformDetect()` runs Avalonia on X11 (XWayland); Avalonia's X11 backend ignores `ExtendClientAreaToDecorationsHint`, so Mutter draws its title bar and `CaptionButtons` stay hidden (`IsExtendedIntoWindowDecorations` is false).
- **Issue 2 feasibility**: feasible, small. `WindowDecorations = None` (as `DialogWindow` already does), always show `CaptionButtons`, keep the existing `BeginMoveDrag`/double-click code, add edge strips calling `BeginResizeDrag` (off when maximized), 1 px outer border. Losses: no WM shadow, no right-click window menu on the bar. Snapping, Super+arrows, Alt+drag keep working (window is still managed).

## 5. Verification & Testing
- Analysis only; nothing built or run.

## 6. Blockers & Open Questions
- User to decide whether to implement either change.

## 7. Next Agent Handoff (Action Items)
1. **Next Task**: Implement the pending-tap change (extension API version 3, needs re-login to test) and/or the undecorated main window, if the user approves.
2. **Commands to Run**: none pending.
3. **Watch Out For**: Commit as Dr_Atomic, no Claude attribution.
