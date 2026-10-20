#!/usr/bin/env python3
"""Test listener for the OG Converter GNOME Shell extension.

Owns io.github.dr_at0mic.OgConverter on the session bus, implements the
io.github.dr_at0mic.OgConverter.Trigger interface and prints every call with a
timestamp. Stand-in for the real (C#) app so the D-Bus contract can be checked.

Usage:  python3 listen.py            (Ctrl+C to quit)
Needs:  python3-gi (installed by default on Ubuntu)
"""
import signal
import sys
import warnings
from datetime import datetime

from gi.repository import Gio, GLib

BUS_NAME = "io.github.dr_at0mic.OgConverter"
OBJECT_PATH = "/io/github/dr_at0mic/OgConverter"
IFACE_XML = """
<node>
  <interface name="io.github.dr_at0mic.OgConverter.Trigger">
    <method name="KeyTapped">
      <arg type="s" name="key" direction="in"/>
      <arg type="i" name="x" direction="in"/>
      <arg type="i" name="y" direction="in"/>
    </method>
    <method name="KeyReleased">
      <arg type="s" name="key" direction="in"/>
    </method>
    <method name="ButtonReleased">
      <arg type="i" name="x" direction="in"/>
      <arg type="i" name="y" direction="in"/>
    </method>
  </interface>
</node>
"""


def log(msg):
    print(f"{datetime.now().isoformat(timespec='milliseconds')} {msg}", flush=True)


def on_call(conn, sender, path, iface, method, params, invocation):
    args = params.unpack()
    log(f"{method}{tuple(args)!r} from {sender}")
    invocation.return_value(None)


def main():
    node = Gio.DBusNodeInfo.new_for_xml(IFACE_XML)
    loop = GLib.MainLoop()

    def on_bus(conn, _name):
        with warnings.catch_warnings():
            warnings.simplefilter("ignore", DeprecationWarning)
            conn.register_object(OBJECT_PATH, node.interfaces[0], on_call, None, None)

    def on_acquired(_conn, name):
        log(f"owning {name}, waiting for calls on {OBJECT_PATH}")

    def on_lost(_conn, name):
        log(f"could not own {name} (another instance or the app running?)")
        loop.quit()

    Gio.bus_own_name(Gio.BusType.SESSION, BUS_NAME, Gio.BusNameOwnerFlags.NONE,
                     on_bus, on_acquired, on_lost)
    try:
        from gi.repository import GLibUnix
        signal_add = GLibUnix.signal_add
    except ImportError:
        signal_add = GLib.unix_signal_add
    for sig in (signal.SIGINT, signal.SIGTERM):
        signal_add(GLib.PRIORITY_DEFAULT, sig, loop.quit)
    loop.run()
    return 0


if __name__ == "__main__":
    sys.exit(main())
