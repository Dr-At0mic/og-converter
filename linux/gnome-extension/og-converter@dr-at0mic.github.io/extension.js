// OG Converter - GNOME Shell helper extension (GNOME Shell 50, ESM format).
//
// On GNOME Wayland an ordinary application cannot see global key presses or
// the pointer position, so it cannot implement OG Converter's
// "drag a file, tap a modifier, get the wheel" gesture by itself. The Shell
// can. This extension watches for that gesture and forwards it to the
// OG Converter app over the session bus (see README.md for the contract).
//
// Detection strategy (see README.md, "Polling vs events"):
//   * Key events of a Wayland client drag-and-drop go straight to the Wayland
//     clients and never reach the Clutter stage, so they cannot be observed
//     with a `captured-event` handler. Instead the seat state is polled with
//     global.get_pointer() -> [x, y, Clutter.ModifierType mask], which does
//     reflect keyboard modifiers and mouse buttons regardless of focus.
//   * Idle: one cheap poll every IDLE_POLL_MS that only checks BUTTON1_MASK.
//     Meta.Dnd 'dnd-enter' (emitted by mutter when any Wayland/XWayland
//     drag-and-drop starts) also switches to fast polling immediately.
//   * While button 1 is held: poll every FAST_POLL_MS and look for modifier
//     edges.
//   * Nothing at all runs (no timers, no signals except a D-Bus name watch)
//     while the OG Converter app is not on the bus.

import Clutter from 'gi://Clutter';
import Gio from 'gi://Gio';
import GLib from 'gi://GLib';

import {Extension} from 'resource:///org/gnome/shell/extensions/extension.js';

// ---- D-Bus contract (keep in sync with README.md and the C# side) ---------
const APP_BUS_NAME = 'io.github.dr_at0mic.OgConverter';
const APP_OBJECT_PATH = '/io/github/dr_at0mic/OgConverter';
const APP_TRIGGER_IFACE = 'io.github.dr_at0mic.OgConverter.Trigger';

const SHELL_OBJECT_PATH = '/io/github/dr_at0mic/OgConverter/Shell';
const SHELL_IFACE_XML = `
<node>
  <interface name="io.github.dr_at0mic.OgConverter.Shell">
    <method name="MoveWindowByTitle">
      <arg type="s" name="title" direction="in"/>
      <arg type="i" name="x" direction="in"/>
      <arg type="i" name="y" direction="in"/>
      <arg type="b" name="found" direction="out"/>
    </method>
    <method name="GetPointer">
      <arg type="i" name="x" direction="out"/>
      <arg type="i" name="y" direction="out"/>
    </method>
    <method name="NudgePointer"/>
    <method name="SetDebug">
      <arg type="b" name="enabled" direction="in"/>
    </method>
    <property name="Version" type="u" access="read"/>
    <property name="AppPresent" type="b" access="read"/>
  </interface>
</node>`;
const SHELL_API_VERSION = 2;

// NudgePointer: how long the pointer stays one pixel off before it goes back.
// Long enough not to be merged with the move back into a single (null) motion.
const NUDGE_BACK_MS = 30;

// ---- Tuning ----------------------------------------------------------------
const IDLE_POLL_MS = 100;  // only looks at BUTTON1_MASK
const FAST_POLL_MS = 20;   // while button 1 is held

// Only run detection while the app owns its bus name. Set to false to make
// the extension always poll (e.g. to test the gesture with debug logging).
const REQUIRE_APP_ON_BUS = true;

const M = Clutter.ModifierType;
const BUTTON1 = M.BUTTON1_MASK;
// Order matters only for the order of emitted calls when several keys change
// within one poll interval.
const KEYS = [
    ['Shift', M.SHIFT_MASK],
    ['Control', M.CONTROL_MASK],
    ['Alt', M.MOD1_MASK],
    // Super shows up as SUPER_MASK (virtual) and/or MOD4_MASK (real) depending
    // on the keymap; accept either.
    ['Super', M.SUPER_MASK | M.MOD4_MASK],
];

function keysDown(mods) {
    const down = new Set();
    for (const [name, mask] of KEYS) {
        if (mods & mask)
            down.add(name);
    }
    return down;
}

function isNoOwnerError(e) {
    return e instanceof GLib.Error &&
        (e.matches(Gio.DBusError, Gio.DBusError.SERVICE_UNKNOWN) ||
         e.matches(Gio.DBusError, Gio.DBusError.NAME_HAS_NO_OWNER));
}

/**
 * Watches the seat for "button 1 held + modifier tapped" and reports edges.
 */
class DragTapDetector {
    constructor({onKeyTapped, onKeyReleased, onButtonReleased, debug}) {
        this._onKeyTapped = onKeyTapped;
        this._onKeyReleased = onKeyReleased;
        this._onButtonReleased = onButtonReleased;
        this._debug = debug;

        this._timerId = 0;
        this._timerInterval = 0;
        this._dnd = null;
        this._dndSignalIds = [];
        this._dndActive = false;
        this._running = false;
        this._resetGesture();
    }

    _resetGesture() {
        this._held = false;          // button 1 currently held (as last polled)
        this._ignored = new Set();   // keys already down when the button went down
        this._down = new Set();      // keys down at the last poll
        this._tapped = new Set();    // keys we sent KeyTapped for and not yet released
        this._anyTap = false;        // at least one KeyTapped during this press
    }

    start() {
        if (this._running)
            return;
        this._running = true;

        // Meta.Dnd: emitted by mutter for Wayland (and XWayland) drag-and-drop.
        // Used only as a hint to switch to fast polling without waiting for the
        // next idle poll.
        try {
            this._dnd = global.backend.get_dnd();
            this._dndSignalIds = [
                this._dnd.connect('dnd-enter', () => {
                    this._dndActive = true;
                    this._debug('dnd-enter');
                    this._tick(); // switches to fast polling if button 1 is held
                }),
                this._dnd.connect('dnd-leave', () => {
                    this._dndActive = false;
                    this._debug('dnd-leave');
                }),
            ];
        } catch (e) {
            this._dnd = null;
            this._dndSignalIds = [];
            console.warn(`[og-converter] Meta.Dnd unavailable: ${e.message}`);
        }

        this._setTimer(IDLE_POLL_MS);
    }

    stop() {
        if (!this._running)
            return;
        this._running = false;
        this._removeTimer();
        if (this._dnd) {
            for (const id of this._dndSignalIds)
                this._dnd.disconnect(id);
        }
        this._dnd = null;
        this._dndSignalIds = [];
        this._dndActive = false;
        this._resetGesture();
    }

    get running() {
        return this._running;
    }

    _removeTimer() {
        if (this._timerId) {
            GLib.Source.remove(this._timerId);
            this._timerId = 0;
            this._timerInterval = 0;
        }
    }

    _setTimer(ms) {
        if (!this._running || (this._timerId && this._timerInterval === ms))
            return;
        this._removeTimer();
        this._timerInterval = ms;
        this._timerId = GLib.timeout_add(GLib.PRIORITY_DEFAULT, ms, () => {
            this._tick();
            return GLib.SOURCE_CONTINUE;
        });
        GLib.Source.set_name_by_id(this._timerId, `[og-converter] poll ${ms}ms`);
    }

    _tick() {
        const [x, y, mods] = global.get_pointer();
        const held = (mods & BUTTON1) !== 0;

        if (!this._held) {
            if (!held)
                return; // idle: the only work is the get_pointer() above
            // Button just went down. Keys already held now (e.g. Shift-click
            // to extend a selection) must not trigger.
            this._held = true;
            this._ignored = keysDown(mods);
            this._down = new Set(this._ignored);
            this._debug(`button1 down at ${x},${y}; ignoring [${[...this._ignored]}]`);
            this._setTimer(FAST_POLL_MS);
            return;
        }

        if (!held) {
            this._debug(`button1 up at ${x},${y}; anyTap=${this._anyTap}`);
            const anyTap = this._anyTap;
            this._resetGesture();
            this._setTimer(IDLE_POLL_MS);
            if (anyTap)
                this._onButtonReleased(x, y);
            return;
        }

        const now = keysDown(mods);
        for (const [name] of KEYS) {
            const was = this._down.has(name);
            const is = now.has(name);
            if (is && !was) {
                if (!this._ignored.has(name)) {
                    this._tapped.add(name);
                    this._anyTap = true;
                    this._onKeyTapped(name, x, y);
                }
            } else if (!is && was) {
                // A key that was down at press time becomes eligible once
                // released, so release + press again during the drag works.
                this._ignored.delete(name);
                if (this._tapped.delete(name))
                    this._onKeyReleased(name);
            }
        }
        this._down = now;
    }
}

export default class OgConverterExtension extends Extension {
    enable() {
        this._debugEnabled = false;
        this._appPresent = false;
        this._stageEventId = 0;
        this._nudgeId = 0;
        this._virtualPointer = null;

        this._detector = new DragTapDetector({
            onKeyTapped: (key, x, y) => {
                this._log(`KeyTapped(${key}, ${x}, ${y})`);
                this._callApp('KeyTapped', new GLib.Variant('(sii)', [key, x, y]));
            },
            onKeyReleased: key => {
                this._log(`KeyReleased(${key})`);
                this._callApp('KeyReleased', new GLib.Variant('(s)', [key]));
            },
            onButtonReleased: (x, y) => {
                this._log(`ButtonReleased(${x}, ${y})`);
                this._callApp('ButtonReleased', new GLib.Variant('(ii)', [x, y]));
            },
            debug: msg => this._log(msg),
        });

        // Our own small service, reachable at the Shell's well-known name
        // (org.gnome.Shell) because it is exported on the Shell's connection.
        this._dbusImpl = Gio.DBusExportedObject.wrapJSObject(SHELL_IFACE_XML, this);
        this._dbusImpl.export(Gio.DBus.session, SHELL_OBJECT_PATH);

        if (REQUIRE_APP_ON_BUS) {
            this._nameWatchId = Gio.bus_watch_name_on_connection(
                Gio.DBus.session, APP_BUS_NAME, Gio.BusNameWatcherFlags.NONE,
                () => this._setAppPresent(true),
                () => this._setAppPresent(false));
        } else {
            this._nameWatchId = 0;
            this._detector.start();
        }
    }

    disable() {
        if (this._nameWatchId) {
            Gio.bus_unwatch_name(this._nameWatchId);
            this._nameWatchId = 0;
        }
        this._detector?.stop();
        this._detector = null;
        this._setStageProbe(false);
        if (this._nudgeId) {
            GLib.Source.remove(this._nudgeId);
            this._nudgeId = 0;
        }
        this._virtualPointer = null;
        if (this._dbusImpl) {
            this._dbusImpl.unexport();
            this._dbusImpl = null;
        }
    }

    _setAppPresent(present) {
        this._appPresent = present;
        this._log(`app ${present ? 'appeared' : 'vanished'} on the bus`);
        if (present)
            this._detector?.start();
        else
            this._detector?.stop();
        this._dbusImpl?.emit_property_changed('AppPresent',
            new GLib.Variant('b', present));
    }

    _log(msg) {
        if (this._debugEnabled)
            console.log(`[og-converter] ${msg}`);
    }

    _callApp(method, params) {
        Gio.DBus.session.call(
            APP_BUS_NAME, APP_OBJECT_PATH, APP_TRIGGER_IFACE, method, params,
            null, Gio.DBusCallFlags.NO_AUTO_START, 2000, null,
            (conn, res) => {
                try {
                    conn.call_finish(res);
                } catch (e) {
                    if (!isNoOwnerError(e))
                        this._log(`${method} failed: ${e.message}`);
                }
            });
    }

    // Debug-only probe: logs whether key events reach the Clutter stage at all
    // (they do not while a Wayland client has keyboard focus / during a client
    // drag-and-drop - that is why detection uses polling).
    _setStageProbe(on) {
        if (on && !this._stageEventId) {
            this._stageEventId = global.stage.connect('captured-event', (_actor, event) => {
                const type = event.type();
                if (type === Clutter.EventType.KEY_PRESS || type === Clutter.EventType.KEY_RELEASE) {
                    const [, , mods] = global.get_pointer();
                    this._log(`stage captured ${type === Clutter.EventType.KEY_PRESS ? 'KEY_PRESS' : 'KEY_RELEASE'} ` +
                        `keyval=0x${event.get_key_symbol().toString(16)} button1=${(mods & BUTTON1) !== 0}`);
                }
                return Clutter.EVENT_PROPAGATE;
            });
        } else if (!on && this._stageEventId) {
            global.stage.disconnect(this._stageEventId);
            this._stageEventId = 0;
        }
    }

    // ---- io.github.dr_at0mic.OgConverter.Shell ----------------------------

    MoveWindowByTitle(title, x, y) {
        const actor = global.get_window_actors().find(a => a.meta_window?.get_title() === title);
        if (!actor) {
            this._log(`MoveWindowByTitle: no window titled "${title}"`);
            return false;
        }
        const win = actor.meta_window;
        win.move_frame(true, x, y);
        // Focus-stealing prevention may have stacked the window under the one
        // the drag started in and flagged it ("... is ready"): put it on top
        if (!win.is_above())
            win.make_above();
        win.raise();
        if (win.demands_attention)
            win.unset_demands_attention();
        this._log(`MoveWindowByTitle("${title}", ${x}, ${y})`);
        return true;
    }

    GetPointer() {
        const [x, y] = global.get_pointer();
        return [x, y];
    }

    // A drag only enters the window under the pointer when the pointer moves.
    // The wheel opens under a still pointer, so it can't see the dragged file
    // until the user moves the mouse; one pixel out and back fixes that.
    NudgePointer() {
        if (!this._virtualPointer) {
            const seat = Clutter.get_default_backend().get_default_seat();
            this._virtualPointer = seat.create_virtual_device(Clutter.InputDeviceType.POINTER_DEVICE);
        }
        this._virtualPointer.notify_relative_motion(GLib.get_monotonic_time(), 1, 0);
        if (this._nudgeId)
            GLib.Source.remove(this._nudgeId);
        this._nudgeId = GLib.timeout_add(GLib.PRIORITY_DEFAULT, NUDGE_BACK_MS, () => {
            this._nudgeId = 0;
            this._virtualPointer?.notify_relative_motion(GLib.get_monotonic_time(), -1, 0);
            return GLib.SOURCE_REMOVE;
        });
        this._log('NudgePointer');
    }

    SetDebug(enabled) {
        this._debugEnabled = enabled;
        this._setStageProbe(enabled);
        console.log(`[og-converter] debug ${enabled ? 'on' : 'off'} ` +
            `(appPresent=${this._appPresent}, detector=${this._detector?.running})`);
    }

    get Version() {
        return SHELL_API_VERSION;
    }

    get AppPresent() {
        return this._appPresent;
    }
}
