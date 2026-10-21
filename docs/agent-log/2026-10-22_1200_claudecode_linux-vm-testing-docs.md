---
date: "2026-10-22 12:00:00"
agent: "claudecode"
model: "Claude Opus 5.5"
session_type: "docs"
status: "done"
sprint: "1.6.0"
related_files:
  - "AGENTS.md"
---

# Agent Session Log: Linux VM testing instructions in AGENTS.md

## 1. Context & Objectives
- **User Goal**: Another agent kept testing the Linux build in WSL. Write down how to work with the Linux build so every agent uses the VM.
- **Starting State**: `feature/target-size-compression` at `110cf45`, clean.

## 2. Actions Completed
- [x] Wrote `docs/LINUX_TESTING.md`, the agent guide for the VMware Workstation Pro VM: why not WSL, VM facts (vmx path, `vmrun` at `D:\Applications\vmware\vmrun.exe`, IP, SSH), start and check the VM with `vmrun`, copy the source, build, `~/run-og-test.sh`, GUI over SSH, the installed copy and the `pkill -f` trap, `gdbus` wheel trigger, screenshots, what needs the user, how to report. `AGENTS.md` §4 is a short VM-only rule that points to it.
- [x] Checked that the VM is reachable, that `~/run-og-test.sh` still exists, and that `vmrun list` / `getGuestIPAddress` work from the host (VM running, IP 192.168.60.128, no snapshots).

## 3. Files Created / Modified / Deleted
| Action | File Path | Description |
|---|---|---|
| Modified | `AGENTS.md` | §4: VM-only rule, points to the guide |
| Created | `docs/LINUX_TESTING.md` | Agent guide for the VMware VM |
| Created | `docs/agent-log/2026-10-22_1200_claudecode_linux-vm-testing-docs.md` | This log |

## 4. Key Decisions
- Put the instructions in `AGENTS.md` and not only in the agent logs, because every agent tool reads `AGENTS.md` first. Until now the VM details only existed in old logs and in Claude Code's private memory.

## 5. Verification
- Docs only, no code changed, so no build or test run.

## 6. Handover
- Not committed yet. Commit it along with the branch work, or on its own if the user asks.
