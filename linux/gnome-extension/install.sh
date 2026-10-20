#!/bin/sh
# Install (or update) the OG Converter GNOME Shell extension for the current user.
set -eu

UUID="og-converter@dr-at0mic.github.io"
SRC="$(cd "$(dirname "$0")" && pwd)/$UUID"
DEST="${XDG_DATA_HOME:-$HOME/.local/share}/gnome-shell/extensions/$UUID"

mkdir -p "$DEST"
cp -f "$SRC/metadata.json" "$SRC/extension.js" "$DEST/"
echo "Installed to $DEST"

# Make sure user extensions are not globally disabled.
if command -v gsettings >/dev/null 2>&1; then
    gsettings set org.gnome.shell disable-user-extensions false || true
fi

if gnome-extensions enable "$UUID" 2>/dev/null; then
    echo "Enabled $UUID"
else
    # The running Shell has not scanned the new folder yet (normal on Wayland).
    # Add it to enabled-extensions directly so it loads after the next login.
    if command -v gsettings >/dev/null 2>&1; then
        current="$(gsettings get org.gnome.shell enabled-extensions)"
        case "$current" in
            *"'$UUID'"*) ;;
            "@as []"|"[]") gsettings set org.gnome.shell enabled-extensions "['$UUID']" ;;
            *) gsettings set org.gnome.shell enabled-extensions "${current%]}, '$UUID']" ;;
        esac
    fi
    echo "Marked $UUID as enabled; it will load after the next login."
fi

echo
echo "GNOME Shell on Wayland only picks up a NEW extension after you log out and back in."
echo "After that, check with:  gnome-extensions info $UUID"
