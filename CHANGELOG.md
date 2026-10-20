# Changelog

What changed in each release. The release workflow copies a version's section into its GitHub release, and
**Settings › System › Check for updates** shows it as "What's new" before updating.

Before tagging `vX.Y.Z`, add a `## [X.Y.Z]` section at the top. The release fails if it's missing.

## [1.5.0]

- **Linux version (beta)**: OG Converter now runs on Linux, as `og-converter-vX.Y.Z-linux-x64.tar.gz` next to the Windows exe.
  The wheel, the dashboard, every dialog, notifications, the tray and templates work. On GNOME, a small helper extension
  (Settings › Desktop integration) brings drag-and-tap; on other desktops, use the file manager's right-click menu.
  **Still being sorted out:** templates with a renderer plugin (`.dll`) are Windows only for now and are drawn with the classic ring
  on Linux, and Linux copies don't update themselves yet (the updater links to the release page). Please report any bugs you notice.
- **Crop**: zoom in with Ctrl+scroll or the new −/+ buttons, scroll or middle-drag to move around, **Fit** to see the whole picture and
  **Width** to fill the width at the top of the selection, which makes long pictures (a README saved as one image) easy to crop.
  Dragging a handle past the edge scrolls the picture.
- Web pages, Markdown and SVG: two jobs that render at the same time no longer delete each other's output (which failed with "The
  browser didn't produce a PDF"), a browser that exits without output gets one more try, and the error now says why the browser failed.

## [1.4.1]

- Developers list: added Bijo Prasad, with a GitHub link, in About and the README.

## [1.4.0]

- **Archive** replaces Zip in the Actions wheel. Pack files and folders as ZIP, 7z, RAR, TAR, tar.gz, tar.bz2, tar.xz, tar.zst, tar.lz4, tar.lz, tar.br, gz, bz2, xz, zst, lz4, lz, br, cpio, ISO or ar. Each format has its own settings: compression level, method (Deflate, LZMA2, PPMd…), solid mode, all CPU cores, zstd long-range matching, Brotli window size and RAR recovery records.
- **Passwords in Archive › Advanced** for ZIP (AES-256 or ZipCrypto), 7z and RAR (AES-256, optionally hiding file names).
- **Protect › Password-protected archive** can now make a 7z or RAR as well as a ZIP, with the compression level and file-name encryption. **Unlock** opens password-protected 7z and RAR archives.
- **Guetzli** JPEG re-encoding: in Compress (as an output format) and in Archive (for every JPEG in the folder). JPEGs come out 20–30% smaller and look the same. Downloaded on request. The dialogs show this PC's CPU and free memory, the memory and time the job needs, and how many pictures to encode at once (each choice with its cost). Pictures bigger than the free memory are skipped unless you tick **Do it anyway**.
- 7-Zip and WinRAR install from the Archive and Protect dialogs when a format needs them.
- Archive and Protect show live progress like conversions do, and quitting OG Converter stops any Guetzli, 7-Zip or WinRAR still running.
- Extract now handles .tar.zst, .tar.lz4, .tar.br, .tar.lz, .zst, .lz4, .br, .lz, .cpio and .iso too.
- **In-app updates**: Settings › System › Check for updates finds the latest release, shows what's new, then downloads it, verifies its checksum and restarts. Settings and templates are kept. OG Converter only contacts GitHub when you click.
- **About**: shows the real version of the build, plus **Source code** (opens the GitHub repository) and **Release notes** buttons.
- **Recommended extras**: on first start and right after installing, OG Converter offers to install FFmpeg if it's missing, and the free LibreOffice suite if neither Microsoft Office nor LibreOffice is found. Settings › Engine has the same Install buttons and now also shows the web browser's status. Nothing installs unless you click Install.
- New wheel defaults: 45% size, with the shortcut hints under the wheel hidden (turn them on in Settings › Advanced).
- Developers list updated, with GitHub links for Anandhu Suresh and Mahesh P S.

## [1.3.0]

- OCR, archives, web page / SVG / HEIC conversion, and command-line conversion (`--convert`).
