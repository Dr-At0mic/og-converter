# OG Converter GNOME Shell extension (prototype)

On Windows, OG Converter's wheel opens when you start dragging a file and tap a
modifier key while still holding the mouse button. GNOME on Wayland doesn't let
an ordinary app see global key presses or the pointer position. The Shell can,
so this small extension (GNOME Shell 50) detects the gesture and sends it to the
OG Converter app over the session D-Bus.

- UUID: `og-converter@dr-at0mic.github.io`
- Files: `og-converter@dr-at0mic.github.io/{metadata.json,extension.js}`
- Test listener: `tools/listen.py` stands in for the app and prints every call.

## Install

```sh
sh install.sh
```

This copies the extension to `~/.local/share/gnome-shell/extensions/og-converter@dr-at0mic.github.io/`
and enables it.

**Known limitation:** GNOME Shell on Wayland only scans for extensions when it
starts. After the first install you must **log out and back in** before the
extension loads. Until then, `gnome-extensions info` reports that it "doesn't
exist". After you update `extension.js` you also need to log out and back in,
because a running Shell keeps the old module.

Check that it loaded:

```sh
gnome-extensions info og-converter@dr-at0mic.github.io      # State: ACTIVE
journalctl --user -b -o cat /usr/bin/gnome-shell | grep -i og-converter   # no JS ERROR lines
```

## Manual test

```sh
python3 tools/listen.py          # terminal 1: pretends to be the app
# optional, makes the extension log its state machine to the journal:
gdbus call --session --dest org.gnome.Shell \
  --object-path /io/github/dr_at0mic/OgConverter/Shell \
  --method io.github.dr_at0mic.OgConverter.Shell.SetDebug true
journalctl --user -f -o cat /usr/bin/gnome-shell | grep og-converter   # terminal 2
```

1. In Files, start dragging a file and keep holding the button.
2. Tap Shift. The listener prints `KeyTapped('Shift', x, y)`, then `KeyReleased('Shift',)`.
3. Release the button. The listener prints `ButtonReleased(x, y)`.
4. Hold Shift, then click or drag (as you would to extend a selection). Nothing is printed.

## D-Bus contract

### Calls from the extension to the app

The app implements this on the session bus:

| | |
|---|---|
| Bus name | `io.github.dr_at0mic.OgConverter` |
| Object path | `/io/github/dr_at0mic/OgConverter` |
| Interface | `io.github.dr_at0mic.OgConverter.Trigger` |

| Method | Meaning |
|---|---|
| `KeyTapped(s key, i x, i y)` | A modifier went down while button 1 was held. That modifier was not already down when the button went down. `key` is one of `"Shift"`, `"Control"`, `"Alt"` or `"Super"`. `x`,`y` are the pointer position in stage (logical) coordinates. |
| `KeyReleased(s key)` | A key that produced `KeyTapped` was released while button 1 was still held. |
| `ButtonReleased(i x, i y)` | Button 1 was released. Sent only if at least one `KeyTapped` happened during that press. This is the drop point. |

```xml
<interface name="io.github.dr_at0mic.OgConverter.Trigger">
  <method name="KeyTapped"><arg type="s" name="key"/><arg type="i" name="x"/><arg type="i" name="y"/></method>
  <method name="KeyReleased"><arg type="s" name="key"/></method>
  <method name="ButtonReleased"><arg type="i" name="x"/><arg type="i" name="y"/></method>
</interface>
```

How the calls are sent and when the extension is active:

- Calls are asynchronous and fire-and-forget, sent with `NO_AUTO_START`. The
  extension ignores the reply. It also ignores "service unknown" and "name has
  no owner" errors without logging them.
- The extension watches the bus name. It only polls while some process owns
  `io.github.dr_at0mic.OgConverter`. When the app isn't running, it has no
  timers and no signal handlers.
- The extension doesn't know about key bindings. It reports which key was
  tapped, and the app maps keys to wheels.
- A key that was held when the button went down becomes eligible again once
  it is released.

### What the extension exports

The extension exports this object on the Shell's own bus connection, so you
call it through `org.gnome.Shell`:

| | |
|---|---|
| Destination | `org.gnome.Shell` |
| Object path | `/io/github/dr_at0mic/OgConverter/Shell` |
| Interface | `io.github.dr_at0mic.OgConverter.Shell` |

| Member | |
|---|---|
| `MoveWindowByTitle(s title, i x, i y) -> (b found)` | Finds the first window whose title matches `title` exactly and calls `move_frame(true, x, y)` on it, then keeps it above other windows, raises it and clears a "demands attention" flag (since version 2). Coordinates are stage (logical) coordinates. |
| `GetPointer() -> (i x, i y)` | Returns the current pointer position in stage coordinates. |
| `NudgePointer()` | Moves the pointer one pixel right and, 30 ms later, back (virtual pointer device). A drag in progress then enters the window now under the pointer (the wheel that just opened there). Since version 2. |
| `SetDebug(b enabled)` | Logs the state machine to the journal and enables a probe for stage key events. |
| property `Version` (u) | Currently `2`. |
| property `AppPresent` (b) | `true` while the app owns its bus name. |

## Design notes and findings

### Polling vs events

Tested on GNOME Shell 50.1 in a headless nested shell with a GTK4 Wayland drag
source:

- While a Wayland client drag-and-drop is in progress, mutter takes keyboard
  focus away from the client (`wl_keyboard.leave`). Key events don't reach the
  Clutter stage either. A `global.stage` `captured-event` handler saw **no**
  key presses during the drag. The stage only sees keys when the Shell itself
  has focus, for example in the overview.
- `global.get_pointer()` returns `[x, y, mods]`. The mask did contain the
  modifier during the drag, so polling caught the Shift tap.
- `global.backend.get_dnd()` (Meta.Dnd) emits `dnd-enter` / `dnd-leave` when a
  Wayland client calls `start_drag`. The extension uses `dnd-enter` to switch
  to fast polling immediately, and to know when the drag has really started
  (see below).

The extension therefore detects the gesture like this:

- **Idle:** it polls every 100 ms, but only while the app is on the bus. Each
  poll is one `get_pointer()` call and a bit test.
- **While button 1 is held:** it polls every 20 ms and looks for modifier
  changes.
- **On `disable()`:** it removes the timer, disconnects the signals, stops
  watching the bus name and unexports the D-Bus object.

A press and release shorter than 100 ms can be missed. That doesn't matter,
because a file drag always lasts longer.

### A key tapped before the drag starts

Pressing a file and tapping the key without moving the mouse is not a drag
yet: Files only starts one when the pointer moves past GTK's drag threshold
(8 px by default). Until then there is no drag data, so a wheel opened right
away would be empty, and it filled in only once the user moved the mouse
(version 2 and earlier). The one-pixel `NudgePointer` doesn't help here,
because it stays below the threshold.

Since version 3 such a tap waits: `KeyTapped` is sent when `dnd-enter` says
the drag has started, at the pointer's position then. If the key was already
let go, `KeyReleased` follows right after, as if the tap had happened then.
If the button goes up without a drag, nothing is sent. In case a drag source
never makes mutter emit `dnd-enter`, the tap is also sent once the pointer
has moved 24 px from where the button went down. Without `Meta.Dnd`, taps are
sent at once, as before.

### Do we need `MoveWindowByTitle`?

On GNOME Wayland, a native Wayland window cannot position itself. The
extension's `MoveWindowByTitle` covers that case; it was tested and moves the
window to the exact frame position. An XWayland (X11) window, especially an
override-redirect one, can usually place itself. However, X11 coordinates only
match stage coordinates without fractional scaling. Also, `XQueryPointer` is
stale while the pointer is over Wayland windows. Because of that, the app
should use the `x`,`y` from `KeyTapped` (or `GetPointer()`), not its own
pointer query. Keep `MoveWindowByTitle` as a fallback for when the wheel window
doesn't land where it was requested.

Security note: any process on the session bus can call `MoveWindowByTitle`. It
can only move windows, but this should be reviewed before release.
