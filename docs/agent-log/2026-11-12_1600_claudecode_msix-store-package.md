---
date: "2026-11-12 16:00:00"
agent: "claudecode"
model: "Claude Opus 5.5"
session_type: "feature"
status: "completed"
sprint: "Microsoft Store release"
related_files:
  - "packaging/msix/AppxManifest.xml"
  - "packaging/msix/build-msix.ps1"
  - "packaging/msix/Assets/"
  - "src/OgConverter.Windows/Services/PackageService.cs"
  - "src/OgConverter.Windows/App.xaml.cs"
  - "src/OgConverter.Windows/MainWindow.System.cs"
  - "src/OgConverter.Windows/MainWindow.Updates.cs"
---

# Agent Session Log: MSIX package for the Microsoft Store

## 1. Context & Objectives
- **User Goal:** build OG Converter as an MSIX for Microsoft Store submission, using the Partner Center identity, and put it in an appropriate folder.
- **Partner Center identity:**
  - Name `DrAtomic.OGConverter`
  - Publisher `CN=59C61989-EB11-480C-9DAE-138946F0898B`
  - PublisherDisplayName `Dr Atomic`
  - PFN `DrAtomic.OGConverter_cqr4nsexcrjqp`
  - Store ID `9PFHZ91JDJF6`
- **Starting State:** `main` at c4218a9 (v1.8.0). No packaging project existed; release.yml ships a self-contained single-file exe.

## 2. Actions Completed
- [x] Added **`PackageService`** (Windows project). It detects package identity (`GetCurrentPackageFullName`), detects startup-task activation (`AppInstance.GetActivatedEventArgs`), and wraps the MSIX `StartupTask` API. It also holds the Store product ID and the `ms-windows-store://` link.
- [x] **Packaged-only branches.** Unpackaged (GitHub exe) behaviour is unchanged:
  - `App.xaml.cs`: skips `UpdateService.CleanupAfterUpdate` / `RefreshUninstallEntryVersion`. A startup-task launch starts hidden in the tray, like `--background`.
  - `MainWindow.System.cs`: shows "Installed from the Microsoft Store" and hides Install / Uninstall / desktop shortcut / Home install banner. "Start with Windows" uses the startup task, with a hint if the user disabled it in Task Manager.
  - `MainWindow.Updates.cs`: shows "Updates come from the Microsoft Store"; the button opens the Store page (Store policy: no self-update outside the Store).
- [x] **`packaging/msix/`:**
  - `AppxManifest.xml`: full-trust desktop app; `windows.startupTask` `OGConverterStartup`, disabled by default; capabilities `internetClient` + `runFullTrust`; Min 10.0.17763 (matches `SupportedOSPlatformVersion`); MaxTested 10.0.26100.
  - `Assets/`: 22 logos generated from `og-converter.ico`, at scale-100/200, plus targetsize 16/24/32/48/256 (native .ico frames) with altform-unplated.
  - `build-msix.ps1`: same `dotnet publish` as release.yml (single-file, self-contained), then layout, MakePri, MakeAppx, then a sha256.
- [x] **Built `dist/store/OGConverter_1.8.0.0_x64.msix`** (68.2 MB) plus `.sha256`. It is unsigned: Partner Center signs Store submissions.

## 3. Verification
- `dotnet build og-converter.sln -c Release`: 0 warnings, 0 errors.
- `og-converter.exe --test`: **ALL VERIFICATION TESTS PASSED**.
- The layout was registered locally (Developer Mode on) and launched as a packaged app:
  - **PFN = `DrAtomic.OGConverter_cqr4nsexcrjqp`** (matches Partner Center).
  - The window opened; Settings showed the Store texts.
  - The Start-with-Windows toggle went Off → On → Off through the StartupTask API.
- The test package was then removed (`Remove-AppxPackage`). The user's own installed copy in `%LocalAppData%\Programs` was not touched.

## 4. Key Decisions & Rationales
- The MSIX packages the **same single-file build** as the GitHub release, for parity with what's tested and shipped.
- Package version = tag + `.0` (1.8.0 → 1.8.0.0), as the Store requires.
- Output goes in `dist/store/` (already git-ignored); the intermediate layout goes in `artifacts/msix/` (git-ignored).

## 5. Store-certification risks to review (not changed)
- **Optional helpers** download or install third-party executables at the user's request:
  - FFmpeg / LibreOffice / 7-Zip via winget.
  - Guetzli downloaded from GitHub into `%LocalAppData%\OGConverter\tools`.
  - Policy 10.2.2 reviewers may question this. Options: keep it and explain in the submission notes; hide the Guetzli download in the packaged build; or link to download pages only.
- **Renderer plugins** load user-supplied `.dll`s (wheel templates). This is worth mentioning in the certification notes.
- `runFullTrust` is a restricted capability. Partner Center asks for a justification: global drag-and-key detection (low-level hooks), Explorer/editor selection via UI Automation, and launching FFmpeg/Office/Edge for conversions.

## 6. Handover / Next Steps
- [ ] Upload `dist/store/OGConverter_1.8.0.0_x64.msix` in Partner Center > Packages; fill in the runFullTrust justification and certification notes (section 5).
- [ ] Commit the code + `packaging/` (not done; the user hasn't asked to commit).
- [ ] Optional: add an MSIX job to release.yml that runs `packaging/msix/build-msix.ps1` on windows-latest.
- Rebuild: `powershell -ExecutionPolicy Bypass -File packaging/msix/build-msix.ps1 -Version <x.y.z>`.
