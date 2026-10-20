---
date: "2026-10-21 00:25:00"
agent: "claudecode"
model: "Claude Opus 5.5 (claude-opus-5-5)"
session_type: "refactor"
status: "in_progress"
sprint: "Linux port"
related_files:
  - "og-converter.sln"
  - "src/OgConverter.Core/Platform/Platform.cs"
  - "src/OgConverter.Windows/Platform/Windows/WindowsPlatform.cs"
  - "src/OgConverter.Linux/Program.cs"
  - "src/OgConverter.Linux/Platform/Linux/LinuxPlatform.cs"
  - "src/OgConverter.Linux/Services/WheelController.cs"
  - "src/OgConverter.Linux/Icons/Icons.cs"
  - "linux/gnome-extension/README.md"
---

# Agent Session Log: Linux port, phase 1 (Core split, GNOME trigger, Linux app in progress)

## 1. Context & Objectives
- **User Goal**: Port OG Converter to Linux, with Windows and Linux living on the same `main` branch. Build it, test the Windows build, and test Linux in the user's VM.
- **Starting State**: `main` at `6394f75` (Release v1.4.1), a single WPF project `og-converter.csproj` at the repo root, Windows only.
- **Ending State**: branch `feature/linux-port` (local only, never pushed) has 3 commits. The repo is split into Core, Windows and Linux projects, and the Windows behaviour is verified unchanged. The GNOME drag-and-tap trigger is proven on the real VM. Three parallel workstreams (engine, wheel, dashboard) stopped half-way when the API session limit hit. Their work is saved as WIP commits on three worktree branches and **not merged yet**.

## 2. Actions Completed
- [x] Analysed feasibility and proposed a plan. The user accepted it: Core + Windows (WPF, unchanged) + Linux (Avalonia), all on `main`. Effort estimate: 6–8 weeks to an MVP, 10–14 to parity.
- [x] Recorded the Windows baseline: build gives 0 warnings / 0 errors and `--test` passes (285 lines).
- [x] Commit `7b5d0e6`, *Split into OgConverter.Core and OgConverter.Windows* (done by a sub-agent, then re-verified by me):
  - Core is plain `net10.0`. Searching it for `System.Windows`, `Microsoft.Win32`, `DllImport`, `ComImport` or `Marshal.` finds nothing.
  - Windows self-test passes. Converted outputs are byte-identical to the old build. All 92 `--snapshot` PNGs are pixel-identical. Only archive sizes differ, because of timestamps.
- [x] Commit `cf09d18`, *Add GNOME Shell extension for drag-and-tap on Wayland*. The user tested it with a real drag in Files, tapping Shift and Ctrl, and the `KeyTapped`, `KeyReleased` and `ButtonReleased` events arrived.
- [x] Commit `5f1af4d`, *Add OgConverter.Linux skeleton (Avalonia)*: Avalonia 12.1.4 project, `Program.cs` with headless `--test` / `--convert`, and stubs for `LinuxPlatform`, `WheelController` and `Icons`. It builds on Windows and on the VM.
- [x] Set up the test VM: SSH key, `ogvm` alias, and all converter tools installed (see §5).
- [~] Three parallel sub-agents, each in its own git worktree. All three were **interrupted by the API session limit** at about 00:10 IST on 2026-10-21. I committed each worktree's state as a WIP commit (see §3 and §7).

## 3. Files Created / Modified / Deleted

### On `feature/linux-port` (committed)
| Action | File Path | Description |
|---|---|---|
| `CREATED` | [og-converter.sln](../../og-converter.sln) | Solution with Core, Windows and Linux (the samples stay out). |
| `MOVED` | `*.cs`, `*.xaml` → [src/OgConverter.Windows/](../../src/OgConverter.Windows) and [src/OgConverter.Core/](../../src/OgConverter.Core) | Moved with `git mv`. The old root `og-converter.csproj` is replaced by `src/OgConverter.Windows/OgConverter.Windows.csproj`. |
| `CREATED` | [src/OgConverter.Core/Platform/](../../src/OgConverter.Core/Platform) | Abstractions: `Platform` (static registry), `IImageBackend` (+ `RasterImage`, `LoadedImage`, `BgraImage`, `RasterFormat`, `ImageEncoding`), `IDesktopServices`, `IOcrEngine`, `IPdfRasterizer`, `IOfficeAutomation`, `IPackageInstaller`, `IUiDispatcher`, `ISystemInfo`, `IWheelRendererHost`. |
| `CREATED` | [src/OgConverter.Windows/Platform/Windows/](../../src/OgConverter.Windows/Platform/Windows) | `WpfImageBackend`, `WindowsDesktopServices`, `WindowsOcrEngine`, `WindowsPdfRasterizer`, `WindowsOfficeAutomation`, `WingetInstaller`, `WpfUiDispatcher`, `WindowsSystemInfo`, and `WindowsPlatform.Register()`, which is the first line of `App.OnStartup`. |
| `CREATED` | [src/OgConverter.Windows/TypeForwards.cs](../../src/OgConverter.Windows/TypeForwards.cs) | Lets plugins built against the old `og-converter.dll` still find the model types. |
| `CREATED` | [src/OgConverter.Core/Models/PixelRect.cs](../../src/OgConverter.Core/Models/PixelRect.cs) | Replaces `System.Windows.Int32Rect` in `ToolParameters.Crop`. |
| `MODIFIED` | [.github/workflows/release.yml](../../.github/workflows/release.yml) | Publishes `src/OgConverter.Windows/OgConverter.Windows.csproj`. It is still Windows only; no Linux job yet. |
| `MODIFIED` | `.gitattributes`, `README.md`, `docs/PLUGIN_GUIDE.md`, `samples/WheelPluginSample/*.csproj` | `*.sh eol=lf`, new build paths, and the plugin reference path. |
| `CREATED` | [linux/gnome-extension/](../../linux/gnome-extension) | `og-converter@dr-at0mic.github.io/{metadata.json,extension.js}`, `install.sh`, `tools/listen.py` (a test listener), and a README with the D-Bus contract. |
| `CREATED` | [src/OgConverter.Linux/](../../src/OgConverter.Linux) | Skeleton: `OgConverter.Linux.csproj`, `Program.cs`, `App.axaml(.cs)`, `Platform/Linux/LinuxPlatform.cs`, and the stubs `Services/WheelController.cs` and `Icons/Icons.cs`, plus `Assets/og-converter.ico`. |

### Uncommitted in the main checkout (this log)
| Action | File Path | Description |
|---|---|---|
| `CREATED` | [AGENTS.md](../../AGENTS.md) | Agent rules plus the project and stack table. |
| `CREATED` | [docs/agent-log/README.md](README.md) | Logging protocol. |
| `CREATED` | this file | Handoff. |

### WIP worktree branches (committed there, NOT merged)
All three branch from `5f1af4d`. The worktrees live under `.claude/worktrees/`, which is untracked; don't commit it.

| Branch / worktree | WIP commit | Builds? | Contents |
|---|---|---|---|
| `worktree-agent-a869eeaa14a703483` (`.claude/worktrees/agent-a869eeaa14a703483`) — **engine** | `5d03cb7` | **No**: `LinuxPlatform.cs(10)` references `LinuxSelfTests`, which doesn't exist yet. | `Platform/Linux/`: `SkiaImageBackend`, `ImageHeaders`, `LegacyEncoders` (BMP/GIF), `TiffCodec`, `PdfiumRasterizer`, `TesseractOcrEngine`, `LinuxDesktopServices`, `LinuxPackageInstaller`, `AvaloniaUiDispatcher`. Core: new `JpegExif.cs`, `PdfFonts.cs` (PDFsharp font resolver) and `InstallHints.cs`, and edits to ConversionEngine, ArchiveService, DocumentEngine, DroppedPaths, ProtectionService, TemplateManager, UpdateService and IImageBackend/Platform. It also moved the Compression, Document, Edit and Everyday verification into Core and added `Windows/Tests/OfficeFixtures.cs`. It was in the middle of "remove those from the Core file and wire hooks". |
| `worktree-agent-a0e267c8b1cb507d1` (`.claude/worktrees/agent-a0e267c8b1cb507d1`) — **wheel** | `bd083c5` | **Yes**: 0 errors, 0 warnings. | `Controls/RadialWheelCanvas.cs`, `Views/WheelOverlay.cs`, `Wheel/{WheelPainter, WheelRenderApi, WheelRendererHost, WheelSnapshot}.cs`, `Wheel/Renderers/{Ring,Glass,Orbit,Neon}Renderer.cs`, `Services/DBusService.cs`, `Services/SoundService.cs`, a real `Services/WheelController.cs`, the real `Icons/Icons.cs`, and `Assets/Fonts/FluentSystemIcons-Regular.ttf` plus its MIT licence. `Program.cs` and the csproj are edited. It was in the middle of fixing the hint-bar layout scaling. |
| `worktree-agent-a3b66a2bbaf107528` (`.claude/worktrees/agent-a3b66a2bbaf107528`) — **dashboard** | `7b01749` | **No**: the XML comment at `OgConverter.Linux.csproj` line 32 contains `--` (MSB4025). | `Themes/{Theme.axaml, Styles.axaml, AppThemeService.cs}`, `MainWindow.axaml`, `MainWindow.Templates.cs`, a `MainWindow.axaml.cs` skeleton, `Controls/IconExtension.cs`, and `Services/{AppIcon, AvaloniaUiDispatcher, DesktopActions, FileManagerIntegration, GnomeExtensionService, LinuxInstallService, LinuxPreferences, NotificationService, PackageHints, SessionBus}.cs`. `App.axaml` is edited. It was about to write the main window code-behind. No dialogs, tray or `--snapshot` yet. |

## 4. Key Decisions & Rationales
- **Architecture (accepted by the user)**: three projects on one branch. WPF stays for Windows and Avalonia is used for Linux. *Rationale*: no risk to the polished Windows app, and the shared Core keeps features from drifting.
- **Rejected**: one Avalonia UI for both platforms (for now), `#if WINDOWS` everywhere, a separate `linux` branch, Flatpak first (its sandbox blocks the hooks and the system tools), and reading `/dev/input` with evdev (keylogger-like).
- **The Wayland finding drives the design**: Ubuntu 26.04 / GNOME 50 has **no X11 session**, so global keyboard hooks are impossible. Instead, the GNOME Shell extension:
  - polls `global.get_pointer()` modifier bits while mouse button 1 is held (a stage `captured-event` handler sees no keys during a Wayland drag-and-drop);
  - calls the app on D-Bus: bus name `io.github.dr_at0mic.OgConverter`, path `/io/github/dr_at0mic/OgConverter`, interface `io.github.dr_at0mic.OgConverter.Trigger` with `KeyTapped(s key, i x, i y)`, `KeyReleased(s key)` and `ButtonReleased(i x, i y)`. `ButtonReleased` is only sent after a tap.
  - exports, on `org.gnome.Shell` at `/io/github/dr_at0mic/OgConverter/Shell`, the methods `MoveWindowByTitle(s,i,i)->b`, `GetPointer()->(ii)` and `SetDebug(b)`, plus `Version` and `AppPresent`. GNOME won't let apps position their own windows; this is the fallback.
  - only runs its timers while the app owns the bus name.
  - *Open item*: any session-bus client can call `MoveWindowByTitle`. Review this before release.
- **Linux trigger flow**: on a key tap the app doesn't know the dragged file yet. It opens a transparent overlay at (x, y) that acts as a drop target, reads the paths on drag-enter, builds the slices, and runs the job on drop. Also `og-converter --wheel <path> [--mode convert|tools|actions]`, used by the file-manager menus and a keyboard shortcut. That command forwards to a running instance through the D-Bus `io.github.dr_at0mic.OgConverter.App` interface (`ShowWheel(s path, s mode)` and `Activate()`), which also gives single-instance behaviour.
- **Icons**: Segoe Fluent Icons can't be shipped, so the wheel agent bundled **Fluent UI System Icons (MIT)**. All icon use goes through `OgConverter.Icons.Icons.Glyph(segoeCodePoint)`.
- **Linux backends**: SkiaSharp (the version Avalonia already pulls in), PDFium for PDF pages, the Tesseract CLI for OCR, and `wl-copy`/`xclip`, `gsettings`, `org.freedesktop.FileManager1` and `xdg-open` for desktop tasks. `Office` and `Packages` installs are not available; the user sees apt/dnf/pacman hints instead. Update asset suffix: `-linux-x64.AppImage`. Plugins built for WPF are Windows only, and Linux gets the 4 built-in renderers.
- **Plan for packaging**: AppImage first (self-update by replacing `$APPIMAGE`), then `.deb`. Not done yet.
- **Core gained** `System.Security.Cryptography.Pkcs` 8.0.2, because PDFsharp's 8.0.1 is vulnerable and broke the zero-warnings build. The Windows app is unaffected.
- **For the user to know**: during the split, `%AppData%\OGConverter\settings.json` was found with `HideInstallBanner=false`. The cause is unknown and no app code sets it. The user may need to dismiss the Install banner again.

## 5. Verification & Testing
- **Windows** (on `5f1af4d`): `dotnet build og-converter.sln -c Release` gives 0 errors and 1 warning. The warning is CS0067 for the unused stub event in `src/OgConverter.Linux/Services/WheelController.cs` and goes away once the wheel branch is merged. `og-converter.exe --test` exits 0, prints 285 lines and passes everything. Baseline outputs are in `C:\Users\User\AppData\Local\Temp\claude\e--Projects-og-converter\84cf164d-e266-4e71-9998-d12809ca681b\scratchpad\` (`baseline-test.txt` is the original build, `verify-mine.txt` is after the split). The scratchpad is temporary; regenerate the baseline from `main` if it's gone.
- **Linux** (VM, on `5f1af4d`): the build passes. `--test` fails immediately with "No image backend is registered", which is expected until the engine branch lands.
- **GNOME extension**:
  - The extension is ACTIVE in the user's real session, with no JS errors.
  - With `tools/listen.py` running, the user did a real drag in Files and tapped Ctrl and Shift. The output was `KeyTapped('Control', 1302, 671)`, `KeyReleased`, `KeyTapped('Shift', 1203, 649)`, `ButtonReleased(1278, 647)`. ✔
- **Test VM**: VMware NAT guest `192.168.60.128`, user `dr-atomic`.
  - `ssh ogvm` uses the key `~/.ssh/og_vm_ed25519`, which is set up in `~/.ssh/config`.
  - `sudo` needs the user's password. Ask the user for it; it is not stored anywhere.
  - Ubuntu 26.04.1, kernel 7.0, GNOME Shell 50.1, Wayland only, 2 vCPU / 5 GB RAM.
  - Installed: dotnet-sdk-10.0 (10.0.113), ffmpeg, tesseract-ocr(+eng), 7zip (`7z`), libarchive-tools (`bsdtar`), xclip, wl-clipboard, poppler-utils, libnotify-bin, LibreOffice writer/calc/impress (`/usr/bin/soffice`), and chromium as a snap (`/snap/bin/chromium`; a snap can only read files under `$HOME`, so keep browser temp files there).
  - Each workstream used its own directory on the VM: `~/og` (skeleton), `~/og-l1`, `~/og-wheel`, `~/og-ui`. `~/og-gnome-extension` holds the extension copy plus `listen.py`.
  - Sync helper: `scratchpad/sync.sh <repo-dir> <remote-dir>` tars `src/`, `og-converter.sln` and `linux/` without `bin`/`obj`/`.git` and unpacks it on the VM. If the scratchpad is gone, re-create it as a one-liner: `tar --exclude=bin --exclude=obj -czf - src og-converter.sln linux | ssh ogvm "rm -rf DIR && mkdir -p DIR && tar -xzf - -C DIR"`.
  - Graphical-session environment for commands run over SSH: `XDG_RUNTIME_DIR=/run/user/1000 DBUS_SESSION_BUS_ADDRESS=unix:path=/run/user/1000/bus WAYLAND_DISPLAY=wayland-0 DISPLAY=:0`.

## 6. Blockers & Open Questions
- The API session limit stopped all three agents. No technical blocker is known.
- The engine and dashboard WIP branches don't build (see §3). Both are small fixes.
- Merge overlaps to resolve:
  - Both the engine and dashboard branches created an `AvaloniaUiDispatcher`, in `Platform/Linux/` and `Services/` respectively. Keep one, preferably the engine's in `Platform/Linux`, and register it in `LinuxPlatform`.
  - All three branches edited `OgConverter.Linux.csproj`. The wheel and engine branches edited `Program.cs`.
  - Both the wheel and dashboard branches created a `Controls/` folder, with different files.
  - The dashboard's `SessionBus.cs` may duplicate the wheel's `DBusService.cs` connection. Use one shared session-bus connection.
- Still to decide with the user: AppImage-only first, or AppImage plus `.deb`? Add a Linux job to `release.yml` now, or later?

## 7. Next Agent Handoff (Action Items)
1. **Read first**: `AGENTS.md` and this log. Then run `git log --oneline main..feature/linux-port` and `git worktree list`.
2. **Finish each workstream on its own worktree branch.** Fix the build first, then complete the scope:
   - **Engine** (`worktree-agent-a869eeaa14a703483`):
     - Create `LinuxSelfTests`, a `PlatformSelfTests` instance wiring the Linux-capable sections, or drop the reference.
     - Finish moving the verifications into Core with hooks.
     - Goal: `og-converter --test` passes on the VM with most sections running.
     - Core fixes still to make: case-sensitive path comparisons (case-insensitive only on Windows), snap-Chromium temp folder under `$HOME`, LibreOffice headless profile, bsdtar/7z names, and platform-appropriate wording ("Windows can't open this picture", winget hints). Windows wording must stay byte-identical.
     - The Windows `--test` must still pass with unchanged image/PDF/Office sizes.
   - **Wheel** (`worktree-agent-a0e267c8b1cb507d1`):
     - Finish the hint-bar scaling.
     - Compare the `--snapshot-wheel <dir>` PNGs with the Windows `og-converter.exe --snapshot <dir>` output.
     - On the VM, verify that `gdbus call --session --dest io.github.dr_at0mic.OgConverter --object-path /io/github/dr_at0mic/OgConverter --method io.github.dr_at0mic.OgConverter.Trigger.KeyTapped Shift 800 500` opens the overlay.
     - Expose `LinuxWheel.Register()` (sets `Platform.WheelRenderers`) and an activation event for the dashboard.
   - **Dashboard** (`worktree-agent-a3b66a2bbaf107528`):
     - Fix the csproj comment.
     - Finish `MainWindow.axaml.cs`: Home with key bindings ("Super" shown, settings value "Win"), test area, Recent activity, GNOME extension status and install, Settings, Templates, System (install to `~/.local`, `.desktop`, icons, autostart, Nautilus/Dolphin/Nemo integration), Updates (AppImage self-update) and Dependencies (package hints).
     - Port all dialogs: Compress (with the Guetzli panel), Archive, Protect, Unlock, PdfSplit, PdfMerge, Crop (`CropCanvas`), Trim (`RangeTimeline`) and Extras. Port the job pipeline (`ProcessJobAsync` / `TryCollectToolParameters` from the Windows `MainWindow.xaml.cs`).
     - Desktop notifications via `org.freedesktop.Notifications`, with `replaces_id` for progress. Tray via Avalonia `TrayIcon`.
     - `--snapshot <dir>` using Avalonia.Headless, compared against the Windows snapshots.
     - Leave `// TODO(merge)` markers where it needs the wheel's API.
   - Each worktree builds on the VM in its own directory (see §5).
3. **Merge** into `feature/linux-port` in this order: engine, wheel, dashboard. Resolve the overlaps in §6 and the `TODO(merge)` sites, then rebuild with 0 warnings and run the Windows `--test` and the Linux `--test` (on the VM). Then run the app on the VM and ask the user for a manual test: drag a file in Files, tap Shift, drop on a slice; also right-click → Scripts → OG Converter.
4. **Then**:
   - AppImage packaging plus a Linux job in `release.yml`, with the asset named `og-converter-vX.Y.Z-linux-x64.AppImage`.
   - README "Platform support" section.
   - Security review of the extension's `MoveWindowByTitle`.
   - Remove the worktrees after merging (`git worktree remove …`).
5. **Watch out for**:
   - Never push or merge to `main` without the user asking.
   - Never store the VM sudo password.
   - Keep Core free of WPF/WinRT/registry/P/Invoke.
   - Avalonia is version **12.1.4**, which has API breaks from 11. Check the real APIs.
   - Use permissive licences only.
