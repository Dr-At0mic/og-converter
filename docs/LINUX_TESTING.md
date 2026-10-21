# Testing the Linux build on the VMware VM

This guide is for AI agents working on OG Converter on the Windows dev machine. It explains how to build and test the Linux app (`src/OgConverter.Linux`) on the VMware Workstation Pro virtual machine.

## Don't use WSL

Don't build or test the Linux app in WSL, and don't report WSL results as Linux results. The app needs things WSL doesn't have:

- a **GNOME Shell** session, which the drag-and-tap extension in `linux/gnome-extension` runs inside;
- a **Wayland** compositor, for the wheel window, screenshots and `wl-copy`;
- a **D-Bus session bus**, which the extension and the notifications use;
- a desktop file manager (Nautilus) for the real drag test.

The real target is Ubuntu with GNOME on Wayland, and that's what the VM runs. A headless `--test` in WSL may build and partly pass, but it doesn't show whether the app works.

## The VM

| | |
|---|---|
| Hypervisor | VMware Workstation Pro on the Windows host |
| VM file | `D:\virtual machine\Ubuntu 64-bit.vmx` (display name "Ubuntu 64-bit") |
| `vmrun` | `D:\Applications\vmware\vmrun.exe` (not on `PATH`) |
| Guest OS | Ubuntu 26.04 LTS, GNOME 50, **Wayland only** (no X11 session) |
| Resources | 2 vCPUs, 6 GB RAM |
| Network | NAT, guest IP `192.168.60.128` |
| User | `dr-atomic` |
| SSH | alias `ogvm` in `~/.ssh/config`, key `~/.ssh/og_vm_ed25519`, no password |

Already installed on the guest: .NET SDK 10, ffmpeg, tesseract-ocr (eng), 7zip (`7z`), libarchive-tools (`bsdtar`), wl-clipboard, xclip, poppler-utils, libnotify-bin, LibreOffice (Writer/Calc/Impress), Chromium (snap).

`sudo` asks for the user's password. Ask the user for it whenever you need it, and never write it to a file, a log or memory.

## 1. Make sure the VM is running

Run these in PowerShell on the host:

```powershell
$vmrun = "D:\Applications\vmware\vmrun.exe"
$vmx   = "D:\virtual machine\Ubuntu 64-bit.vmx"

& $vmrun -T ws list                      # is it running?
& $vmrun -T ws start $vmx gui            # start it (opens the Workstation window)
& $vmrun -T ws getGuestIPAddress $vmx -wait   # wait until the guest has network
```

Then check SSH: `ssh -o ConnectTimeout=5 ogvm echo ok`.

- If the IP is no longer `192.168.60.128`, update `HostName` under `Host ogvm` in `~/.ssh/config` and tell the user.
- Start the VM with `gui`, not `nogui`. The user logs in to the GNOME desktop in that window, and most GUI tests need a logged-in session.
- After a cold boot, nobody is logged in to GNOME yet, so there's no `/run/user/1000/bus` and no `wayland-0`. Headless parts of `--test` still run. For anything with GUI, D-Bus, the extension or the clipboard, ask the user to log in.
- Don't stop, suspend, reset or revert the VM, and don't take or delete snapshots, unless the user asks you to. The user's own session and their installed copy of the app run on it.

## 2. Copy the source over

Run this from the repo root in Git Bash on the host. It copies the working tree, without `bin`/`obj`/`.git`, to `~/og` on the VM:

```sh
tar --exclude=bin --exclude=obj -czf - src og-converter.sln linux | ssh ogvm "rm -rf ~/og && mkdir -p ~/og && tar -xzf - -C ~/og"
```

To test two builds side by side, use another folder name, e.g. `~/og-mybranch`.

Don't move files with VMware drag-and-drop, shared folders or the shared clipboard. A 111 MB single-file build was silently truncated that way and crashed on start. If you copy a published binary, use `scp` and compare `sha256sum` on both sides.

## 3. Build

```sh
ssh ogvm "cd ~/og && dotnet build src/OgConverter.Linux -c Release"
```

The output goes to `~/og/src/OgConverter.Linux/bin/Release/net10.0/og-converter`. The build must have 0 warnings, the same rule as on Windows.

## 4. Run the self-test

`~/run-og-test.sh` on the VM sets the desktop session environment, runs `og-converter --test` from the folder above, and writes everything to `~/og-test.txt`, with `exit=N` on the last line:

```sh
ssh ogvm "~/run-og-test.sh"                # all sections
ssh ogvm "~/run-og-test.sh compression"    # one section
ssh ogvm "tail -40 ~/og-test.txt"
```

A full run takes several minutes, so run it with `run_in_background`. If you built into a folder other than `~/og`, copy the script and change its `cd` line.

Known issue: `wl-copy` can hang when the GNOME session is idle or locked, and then the full run stops at the Base64 test (documents section). Run single sections, or ask the user to wake the VM.

The Linux test doesn't replace the Windows test. Every change still needs the Windows `og-converter.exe --test` (see `AGENTS.md`).

## 5. Run the GUI from SSH

GUI programs started over SSH need the desktop session's environment:

```sh
export XDG_RUNTIME_DIR=/run/user/1000
export DBUS_SESSION_BUS_ADDRESS=unix:path=/run/user/1000/bus
export WAYLAND_DISPLAY=wayland-0
export DISPLAY=:0                       # XWayland
export XAUTHORITY=$(ls /run/user/1000/.mutter-Xwaylandauth.* | head -1)   # for xprop/xwininfo/x11grab
```

Put these in a small script on the VM and run that script, instead of writing long `ssh ogvm '...'` one-liners.

**The user's installed copy** runs as `~/.local/share/og-converter/app/og-converter --background`, and only one instance owns the D-Bus name. Stop it before you start your build, and start it again when you're done:

```sh
pkill -x og-converter          # in a script on the VM, see warning below
# ... test your build ...
nohup ~/.local/share/og-converter/app/og-converter --background >/dev/null 2>&1 &
```

> **Never run `pkill -f og-converter` inside `ssh ogvm '...'`.** The pattern also matches the SSH shell's own command line, which contains "og-converter", so it kills your SSH session. Use `pkill -x` / `pgrep -x`, or put the command in a script file.

**Open the wheel without a real drag.** The GNOME extension sends these D-Bus calls, and you can send them yourself:

```sh
gdbus call --session -d io.github.dr_at0mic.OgConverter -o /io/github/dr_at0mic/OgConverter \
  -m io.github.dr_at0mic.OgConverter.Trigger.KeyTapped Shift 700 400
gdbus call --session -d io.github.dr_at0mic.OgConverter -o /io/github/dr_at0mic/OgConverter \
  -m io.github.dr_at0mic.OgConverter.Trigger.ButtonReleased 700 400
```

**Screenshots.** Avalonia windows run through XWayland, so you can capture one window:

```sh
xwininfo -root -tree | grep -i "og converter"      # find the window id
ffmpeg -y -f x11grab -window_id <id> -i :0 -frames:v 1 /tmp/shot.png
```

Then copy it to the host with `scp ogvm:/tmp/shot.png <scratchpad>/` and look at it.

## 6. Things that need the user

These can't be automated from SSH. Ask the user and say exactly what to do:

- **Real drag-and-tap**: drag a file in Files (Nautilus), tap Shift or Ctrl while dragging, then drop on a wheel slice.
- **GNOME extension changes**: on Wayland, GNOME Shell only loads them at the next login. Copy the extension to `~/.local/share/gnome-shell/extensions/og-converter@dr-at0mic.github.io/`, then ask the user to log out and back in.
- **Logging in** after the VM boots, and the `sudo` password.

## 7. Report results

In your agent log (`docs/agent-log/`), state that the Linux results came from the VM. Include the commit you tested, the sections you ran, the `exit=` line, and any failures with their output. Don't call the Linux build verified if it only ran in WSL or only built.
