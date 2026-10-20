# og-converter — AI Agent Instructions & Guidelines

Welcome to the **og-converter** workspace. Every AI agent (Google Antigravity, Claude Code, OpenAI Codex / ChatGPT, Cursor, Windsurf, Copilot, etc.) working on this repository **MUST** adhere to the following rules:

---

## 1. Project Context & Status

- **Project Description**: OG Converter, a zero-click offline file converter. Drag a file, tap a modifier key, and a radial wheel opens at the cursor (Convert / Tools / Actions). Windows app released; the Linux app (branch `feature/linux-port`) is a beta: everything works except renderer plugins (Windows only for now).
- **Primary Tech Stack**: .NET 10, C#.

| Folder | What it is |
|---|---|
| [src/OgConverter.Core](src/OgConverter.Core) | `net10.0`, no UI and no Windows APIs: models, conversion/compression/archive/PDF/document services, templates, CLI, portable self-test. OS-specific work goes through the interfaces in `Platform/`. |
| [src/OgConverter.Windows](src/OgConverter.Windows) | WPF app (`og-converter.exe`). `Platform/Windows/` implements the Core interfaces. |
| [src/OgConverter.Linux](src/OgConverter.Linux) | Avalonia 12 app (`og-converter`), in progress. `Platform/Linux/` implements the Core interfaces. |
| [linux/gnome-extension](linux/gnome-extension) | GNOME Shell extension that sends drag-and-tap key events to the Linux app over D-Bus. |
| [samples/](samples) | Wheel-template and renderer-plugin samples (build on their own). |

- **Key Architecture & Standards**:
  - Core must stay free of `System.Windows`, WinRT, `Microsoft.Win32`, P/Invoke and COM. Platform code belongs in the app projects behind the `Platform` interfaces.
  - Windows behaviour must not change because of Linux work. The safety net is the self-test: build `og-converter.sln -c Release`, then run `src/OgConverter.Windows/bin/Release/net10.0-windows10.0.19041.0/og-converter.exe --test` directly (it's a WinExe; `dotnet run` swallows the output). It must pass, with image/PDF/Office output sizes unchanged. Archive sizes vary between runs because of timestamps.
  - PDFsharp is pinned to `[6.2.4]` (last MIT release). Only use permissive licences (MIT/Apache/BSD/OFL); no ImageSharp, GPL or AGPL.
  - Match the surrounding code style: `///` summaries, comment density, naming.
  - Other docs: [README.md](README.md), [CONTRIBUTING.md](CONTRIBUTING.md), [docs/TEMPLATES.md](docs/TEMPLATES.md), [docs/PLUGIN_GUIDE.md](docs/PLUGIN_GUIDE.md).

---

## 2. Mandatory Agent Logging Protocol (ALP)

To ensure zero context loss when switching between different AI agents or sessions:

1. **Before starting a task**:
   - Check the `docs/agent-log/` directory.
   - Read the most recent log file to understand what was accomplished, what decisions were made, and what state the workspace was left in.

2. **After completing a task or conversation**:
   - Create a new log file in `docs/agent-log/` following the naming convention:
     ```
     docs/agent-log/YYYY-MM-DD_HHMM_<agent-name>_<short-slug>.md
     ```
   - Follow the template defined in [docs/agent-log/README.md](docs/agent-log/README.md).
   - Document:
     - Actions completed
     - Files created / modified / deleted
     - Key decisions and rationales (especially user answers)
     - Test and verification status
     - Clear handover notes / action items for the next agent

---

## 3. Core Development Rules

- **Zero Breakage**: Always run compiler, linter, and unit test suites before completing tasks.
- **Minimal Diffs**: Prefer targeted modifications over large rewrites.
- **Traceability**: All architectural decisions and trade-offs must be logged in `docs/agent-log/`.
