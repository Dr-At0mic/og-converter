---
date: "2026-10-21 02:43:32"
agent: "claudecode"
model: "Claude Opus 5.5"
session_type: "documentation"
status: "completed"
sprint: "Linux port"
related_files:
  - "README.md"
  - ".github/workflows/release.yml"
  - "CHANGELOG.md"
---

# Agent Session Log: Linux beta docs, release workflow, commit and push

## 1. Context & Objectives
- **User Goal**: Update the docs and the release workflow for the Linux build, which is a **beta**: everything works except custom templates with renderer plugins, which will be sorted out soon; invite bug reports. Commit and push to GitHub (account Dr-At0mic, `gh` already logged in). **No `Co-Authored-By: Claude` lines in commit messages.**
- **Starting State**: Uncommitted work from the two previous logs today (browser rendering, wheel stacking/nudge, crop zoom).

## 2. Actions Completed
- [x] README: Windows and Linux (beta) platform note with the beta notice and an issues link; Linux note under Wheel Templates; Linux download line; Linux publish command; new **🐧 Linux (beta)** section (what works, current limitations, install and run, GNOME extension, requirements with one apt line, config folder); architecture tree adds `OgConverter.Linux` and `linux/gnome-extension`; Contributing paragraph updated.
- [x] `docs/TEMPLATES.md`, `docs/PLUGIN_GUIDE.md`: plugins are Windows only for now (drawn with the `ring` renderer on Linux), Linux templates folder.
- [x] `CONTRIBUTING.md`: building and testing on both platforms, Core rules, what to include in a Linux bug report.
- [x] `CHANGELOG.md`: `## [Unreleased]` section (Linux beta, crop zoom, browser rendering fix). **Rename it to the version before tagging**; the workflow fails without a `## [X.Y.Z]` section.
- [x] `.github/workflows/release.yml`: new `linux` job (ubuntu-latest) publishes the single-file `linux-x64` build, packs `og-converter-vX.Y.Z-linux-x64.tar.gz` plus `.sha256`, uploads it as an artifact; the `release` job (`needs: linux`) downloads it and attaches it. Release notes now cover both platforms and include the Linux beta notice.
- [x] `AGENTS.md`: status line says Linux is a beta.
- [x] Committed as `Dr_Atomic <92196795+Dr-At0mic@users.noreply.github.com>` without Claude co-author trailers, and pushed `feature/linux-port`.

## 3. Files Created / Modified / Deleted
| Action | File Path | Description |
|---|---|---|
| `MODIFIED` | `README.md`, `CONTRIBUTING.md`, `CHANGELOG.md`, `AGENTS.md` | See above. |
| `MODIFIED` | `docs/TEMPLATES.md`, `docs/PLUGIN_GUIDE.md` | Linux notes. |
| `MODIFIED` | `.github/workflows/release.yml` | Linux job and release notes. |
| `CREATED` | `docs/agent-log/2026-10-21_0243_claudecode_linux-beta-docs-release.md` | This log. |

## 4. Key Decisions & Rationales
- **`.tar.gz`, not AppImage**: a raw binary loses its executable bit when downloaded; an AppImage needs extra packaging that isn't built yet. The Linux updater already handles non-AppImage copies (links to the release page), and only looks for `-linux-x64.AppImage` for in-place updates.
- **`[Unreleased]` changelog section**: the version number is the user's call.
- **Pushed the feature branch only**: no merge to `main` and no tag, so nothing is released until the user decides.
- **Commit author**: the repo's git config still has the work identity; commits were made with `-c user.name/-c user.email` for Dr_Atomic, matching the earlier history rewrite.

## 5. Verification & Testing
- **Workflow**: YAML parses; the Linux job hasn't run on GitHub yet (it runs on the next tag or manual dispatch).
- **Code**: unchanged in this step; builds and self-tests from the previous log still apply (0 warnings, Windows self-test identical to baseline).

## 6. Blockers & Open Questions
- Before the next release: rename `## [Unreleased]` in CHANGELOG.md, merge `feature/linux-port` into `main`, then tag.
- Open issues: Linux renderer plugins; AppImage + in-place updates; drag-and-tap outside GNOME; flaky 7z password check; concurrent jobs sharing an output name; random Windows "browser didn't produce a PDF".

## 7. Next Agent Handoff (Action Items)
1. **Next Task**: Wait for the user's retest of the wheel fixes (extension v2 needs a re-login) and for bug reports.
2. **Commands to Run**: Optionally trigger the release workflow manually to check the Linux job before a real release.
3. **Watch Out For**: Never add `Co-Authored-By: Claude` to commits in this repo; commit as Dr_Atomic.
