# OG Converter ⚡
> The zero-click offline file converter for Windows (inspired by Tangerine for Mac).

*Powered by pure boredom, caffeine, and living in a world with no superpowers, no magic, and no zombie apocalypse to save us from desk jobs.*

Drag a file anywhere in Windows, tap your configured hotkey (e.g. **Shift**), and drop. Turn files into the formats you need without breaking your flow. Everything happens 100% locally on your PC.

![OG Converter home screen](Assets/public/og-home.png)

> **Platform support:** this version runs on **Windows only** (Windows 10 1809+ / Windows 11).
> A **Linux version is coming soon**: work starts in a few days. If you'd like to help build it, please reach out (see [Contributing](#-contributing)).

> **Project status:** this is **not a fully optimized release** yet. What it does aim for is staying light while it waits in the background.
> Measured on Windows 11 with the self-contained release build (see [Memory & startup](#-memory--startup) for details):
> **~64 MB private memory (~108 MB working set)** waiting in the tray, **~80 MB (~130 MB)** with the dashboard open.
> Heavy jobs (video, audio, GIF, WebP/AVIF/JPEG XL) run in a separate FFmpeg process, which uses extra memory only while
> it's working and exits when done.
>
> Found a bug, or hit any difficulty while using it? Please **[open an issue](../../issues)** describing what happened.
> I'll try to resolve it as soon as I get time.

---

## ✨ Key Features

- **Zero-Click Drag & Drop**: Drag a file from File Explorer, the Desktop or your **code editor's file tree** (VS Code, Cursor, Windsurf, Antigravity), tap a modifier key, and the radial wheel appears right at your cursor.
- **Three wheels, rebindable keys** (**Shift**, **Ctrl**, **Alt**, **Win**, **Space**):
  - `⇧ Shift`: **Convert**: images, video, audio, PDF, Word/Excel/PowerPoint, OpenDocument, Markdown/README, archives.
  - `Ctrl`: **Tools**: Compress, **Crop** (images & video, on a live preview), **Trim** (video, start to end), resize, grayscale, remove EXIF, mute, 720p, PDF split/merge/rotate/protect, and more.
  - `Alt`: **Actions**: Copy Path, Show in Explorer, Terminal, SHA-256, Duplicate, Zip, Base64, Protect/Unlock. Work on folders too.
  - `Esc`: cancel the wheel at any time.
- **Lossless & lossy compression for every file type** (see below).
- **Crop & Trim** with a preview window: draw the area to keep (free or locked to 1:1, 4:3, 16:9, 9:16…), or pick a video's start and end on a timeline with the first and last frames shown (see below).
- **README → PDF, Word, HTML, RTF or plain text**, with the screenshots it links to and GitHub-style layout.
- **Password protection**: PDF (AES-256), Office (Agile AES-256), encrypted `.ogx` files (AES-256-GCM / ChaCha20-Poly1305 + Argon2id), password ZIPs.
- **Wheel templates**: OG Classic, macOS-style **Aqua** and **Graphite**, Dock-style **Orbit** and synthwave **Neon Night**, each with its own shapes, animations and sounds. The whole app re-themes to match. Make your own with a `template.json` or a renderer plugin: see [docs/TEMPLATES.md](docs/TEMPLATES.md).
- **Settings**: output folder, wheel size (40–135%), sound feedback, overwrite behaviour, toasts, shortcut hint bar, reduce motion.
- **Run portable or install**: the exe runs as-is; **Settings › System › Install** (or the banner on Home) installs it per-user (no admin), adds it to Start and Windows Settings › Apps, and can **start it with Windows** straight to the tray.
- **100% Offline & Private**: no uploads, no telemetry, no account.
- **System Tray**: sits quietly in the notification area (≈64 MB private memory when idle).

---

## ⌨️ Shortcuts

Start dragging a file in File Explorer, then tap a key while still holding the mouse button. The wheel opens at your cursor; drop on a slice to run it. Each key can be rebound on the Home screen.

**In a code editor** (VS Code, Cursor, Windsurf, Antigravity and other VS Code-based editors), press and hold on a file or folder in the editor's file tree and tap the key the same way. OG Converter reads the item under the cursor (or the selected one) from the editor's accessibility tree, so it works even before the drag starts. Other editors work by dragging the file onto the wheel.

### `Shift`: Convert
Change the file's format. For a PDF: Word, PNG, JPG, WebP, TIFF, or plain text.

![Convert wheel (Shift) on a PDF](Assets/public/og-short-cut-convert.png)

### `Ctrl`: Tools
Edit or optimize the file. For a PDF: Compress, Split / Extract, Merge, Rotate, Cover Image, Remove Metadata, Protect. For an image: Compress, Crop, Half Size, Grayscale, Remove EXIF. For a video: Compress, Trim, Crop, Mute, 720p, First 10s.

![Tools wheel (Ctrl) on a PDF](Assets/public/og-short-cut-tools.png)

### `Alt`: Actions
Quick file actions: Copy Path, Show in Explorer, Terminal, SHA-256, Duplicate, Zip, Base64, Protect. SHA-256 copies the checksum to the clipboard and shows it in Recent activity (hover for the full value).

![Actions wheel (Alt) on a PDF](Assets/public/og-short-cut-actions.png)

Press `Esc` at any time to close the wheel.

The bar under the wheel lists these keys. It shrinks with the wheel, and you can hide it in **Settings › Advanced › Shortcut hints under the wheel** once you know them.

---

## 🗜️ Compression

Drag any file, open the **Tools** wheel (`Ctrl`), choose **Compress**. A dialog lets you pick **Lossless** or **Lossy**, start from a preset, and fine-tune everything. Your last choices are remembered for each file type, and a live summary shows exactly what will happen. If a setting can't make the file smaller, nothing is written and you're told why.

| File type | Lossless | Lossy |
|---|---|---|
| **JPEG** | Metadata removed (GPS, camera, XMP); image data byte-for-byte identical; orientation preserved | JPEG / WebP / AVIF / JPEG XL with quality slider, resize, EXIF rotation applied |
| **PNG** | Optimized PNG (max zlib, adaptive filters), WebP lossless, JPEG XL lossless | **Palette PNG like TinyPNG/pngquant** (dithered, 16–256 colors), WebP, AVIF, JPEG XL, JPEG |
| **BMP / TIFF / WebP / AVIF** | PNG, Deflate TIFF, WebP lossless, JPEG XL lossless | WebP, AVIF, JPEG XL, JPEG, resize |
| **GIF** | Animated WebP (lossless) | Optimized palette, fewer colors, frame-rate cap, resize, or animated WebP |
| **SVG** | Minify (comments, editor data, metadata removed) or `.svgz` | n/a |
| **Video** | Remux & clean (instant), FFV1 archival, H.264 / H.265 lossless | **H.264, H.265/HEVC, AV1 (SVT-AV1), VP9**; constant quality *or* **target file size (two-pass)**; resolution, frame rate, speed, audio; **GPU encoding** (NVIDIA NVENC, Intel Quick Sync, AMD AMF, auto-detected) |
| **Audio** | FLAC (level 12), Apple Lossless (ALAC), or tags/cover removed without re-encoding | **Opus**, AAC, MP3 (LAME VBR); bitrate, mono, sample rate |
| **PDF** | Streams re-compressed at max, uncompressed fonts/content deflated, thumbnails and unused objects removed | Images downsampled to a DPI (Screen 72 / eBook 150 / Print 300) and re-encoded, optional grayscale |
| **Word / Excel / PowerPoint, ODT / ODS / ODP** | Package re-zipped at max, PNG pictures optimized | Pictures resized like Office's *Compress Pictures* (Email 96 / Web 150 / Print 220 / HD 330 ppi) |
| **ZIP / TAR** | ZIP re-packed at maximum; TAR → `.tar.gz` / `.tar.br` | n/a |
| **Everything else** (text, logs, CSV, JSON…) | ZIP, GZip, **Brotli**; JSON minify | n/a |

**Presets** include *Discord 10 MB*, *WhatsApp 16 MB* and *Email 25 MB* for video, *Voice/podcast* for audio, and the classic PDF and Office resolution presets. Long jobs show a progress toast.

Notes:
- WebP, AVIF, JPEG XL, palette PNG, GIF, video and audio need **FFmpeg** (the full build includes every encoder used). JPEG, PNG-lossless, TIFF, PDF, Office and archive compression work without it.
- JPEG XL and AVIF give the smallest images but aren't supported by every app yet; the dialog tells you when that matters.

---

## ✂️ Crop & Trim

**Crop** (Tools wheel, images and videos) opens a window with the picture, or a frame of the video:
- Drag the handles to resize the area, drag inside it to move it, or draw a new one. Arrow keys nudge it (Shift = 10 px), **Enter** crops.
- Lock the shape to **Free, Original, Square, 4:3, 3:2, 16:9, 4:5 or 9:16**. The exact pixel size is shown live; **Reset** selects everything again.
- Videos have a slider to choose which frame to preview. The sound is kept as it is; width and height are rounded to even numbers, as video encoding requires.
- Saved next to the original as `name_cropped.ext`, in the same format. Phone photos are cropped the right way up and JPEGs keep their camera details. Animated GIFs keep every frame.

**Trim** (Tools wheel, videos) keeps only the part between a start and an end:
- Drag the two handles on the timeline, slide the whole selection, or type times like `1:05`, `65.5` or `1:02:03`. The first and last frames you'll keep are shown side by side.
- **Exact** (default): cut to the frame, re-encoded. **Fast cut**: instant and original quality, but it snaps to the nearest keyframe, so the clip may start a little early.
- Saved as `name_trim_1m05s-1m30s.mp4`.

Both need **FFmpeg** for video. Image crop works without it, except saving WebP/AVIF in their own format (they're saved as PNG instead).

---

## 📝 Markdown & README files

Drag a `README.md` (or any `.md`) and tap **Shift**:

| Output | How |
|---|---|
| **PDF** | Printed by Microsoft Edge (built into Windows; Chrome works too), so code blocks, tables, task lists, note boxes and images look like GitHub. Falls back to Word or LibreOffice. |
| **DOCX / ODT / RTF** | Through Word or LibreOffice |
| **HTML** | A standalone page with the same styling |
| **TXT** | Plain text with the Markdown symbols removed |

Pictures linked with relative paths (`![shot](docs/img/screen.png)` or `<img src="...">`) are included wherever the output is saved.

---

## 🎨 Wheel Templates

A template sets how the wheel **looks, moves and sounds**, and the whole app (Home, Settings, dialogs, notifications) switches to its colors with it.

| Template | Style | Motion & sound |
|---|---|---|
| **OG Classic** | The original graphite ring | Pop, crisp clicks |
| **Aqua** | macOS light: frosted rounded tiles, system blue / orange / purple | Spring, glassy taps |
| **Graphite** | macOS dark mode | Spring, glassy taps |
| **Orbit** | Dock-style bubbles that magnify under the cursor | Bloom, bubbly bloops |
| **Neon Night** | Synthwave HUD: glowing outlines, tick marks, mono labels | Spin, synth blips |

- **Switch** in **Settings › Wheel templates** (hover a card to preview its animation) or with the template picker on **Home**.
- **Add community templates** with **Add template…**: pick a `template.json`, a `.zip` / `.ogtemplate` package or a plugin `.dll`, or drag one onto the gallery. They're stored in `%AppData%\OGConverter\Templates`.
- **Make your own**: a `template.json` needs no code (colors, shape, animation, sound pack or your own `.wav` files). A **renderer plugin** (`IWheelRenderer`) can draw a completely new wheel. **Export** saves the active template as a starting point.
- **Samples**: [`samples/templates/sunset/template.json`](samples/templates/sunset/template.json) (no code) and [`samples/WheelPluginSample`](samples/WheelPluginSample) (a plugin; build it, then add `WheelPluginSample.dll` from its `bin\Release\...` folder).

- **Step-by-step guide** (every setting, sounds, animations, writing a plugin): **[docs/PLUGIN_GUIDE.md](docs/PLUGIN_GUIDE.md)**
- **Quick reference**: **[docs/TEMPLATES.md](docs/TEMPLATES.md)**

> Plugins are programs and run with the same access as OG Converter. The app asks before adding one; only install plugins from people you trust.

---

## 📊 Memory & startup

Measured on Windows 11 with the self-contained release build (`dotnet publish` command below):

| State | Private memory | Working set | CPU |
|---|---|---|---|
| **Started with Windows**, waiting in the tray (`--background`) | ~64 MB | ~108 MB | ~0.6 s to start, then effectively idle |
| **Dashboard open** | ~80 MB | ~130 MB | window shown in ~0.55 s |
| After a session of conversions and dialogs | ~110 MB | ~180 MB | idle |

- **Private memory** is what OG Converter alone uses. The working set also counts shared Windows and .NET libraries, which other apps use too.
- In the tray, the keyboard and mouse hooks only wake the app for its own keys. Mouse movement is ignored until the wheel is open.
- Converting, crop previews and trimming start FFmpeg, Edge or Office as separate processes, which exit when done.
- In a VS Code-based editor, the first lookup turns on the editor's accessibility support, which can make the *editor* use a little more memory.

---

## 🚀 How to Run

### Download
Grab `og-converter-vX.Y.Z-win-x64.exe` from [Releases](../../releases) and run it: no installer or .NET runtime needed.
It runs portable; to install it, see [Install, start with Windows, uninstall](#install-start-with-windows-uninstall) below.

### Prerequisites
- Windows 10 (1809) or later
- [.NET 10 SDK](https://dotnet.microsoft.com/) (for building)
- [FFmpeg](https://ffmpeg.org/) on `PATH` or next to the exe: `winget install Gyan.FFmpeg`
- Optional, for Office conversions: Microsoft Office, or LibreOffice (`winget install TheDocumentFoundation.LibreOffice`)
- Markdown → PDF uses Microsoft Edge, which ships with Windows

### Run in Development
```powershell
# From the repository root
dotnet run --project og-converter.csproj
```

### Install, start with Windows, uninstall
The downloaded exe runs without installing. To install, use **Settings › System › Install** (or the banner on Home): it copies OG Converter to
`%LocalAppData%\Programs\OG Converter`, adds a Start menu entry and an entry in Windows Settings › Apps, and turns on **Start with Windows**
(starts in the tray via `og-converter.exe --background`; toggle it in Settings › System). Uninstall from Settings › System or Windows Settings › Apps;
settings and templates in `%AppData%\OGConverter` are kept.

### Run the Self-Test Suite
```powershell
dotnet run --project og-converter.csproj -- --test
```
Covers conversions, PDF tools, protection, and compression for every file type and mode: lossless results are checked pixel-for-pixel, target sizes are measured, and PDFs and Office files are validated after compression.
It also crops every image format, animated GIFs and video, trims video (exact and fast), reads paths from every kind of drag source (Explorer, editor path text, `file://` links),
and converts a README with screenshots to every Markdown output, checking the pictures made it into the PDF.
It also checks wheel templates: every built-in renders in every wheel at 40% and 100%, imports are validated and sandboxed, and bad files are rejected
(against a temporary templates folder, never your own). To include the plugin checks, build the sample plugin and point `OGCONVERTER_PLUGIN_SAMPLE` at it:

```powershell
dotnet build samples\WheelPluginSample -c Release
$env:OGCONVERTER_PLUGIN_SAMPLE = "$PWD\samples\WheelPluginSample\bin\Release\net10.0-windows10.0.19041.0\WheelPluginSample.dll"
dotnet run --project og-converter.csproj -- --test
```

`dotnet run --project og-converter.csproj -- --snapshot <folder>` renders every screen, dialog and installed template to PNG for visual review.

### Publish a Release Build (single self-contained `.exe`)
```powershell
dotnet publish og-converter.csproj -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish
```
Output: `publish\og-converter.exe` (≈174 MB, no .NET install needed). It's the same command the release workflow runs; add `-p:Version=1.2.3` to stamp a version
(shown in Settings › System and Windows Settings › Apps). Name the `.csproj` explicitly: the sample plugin under `samples/` is a second project.
There's no separate installer: the exe installs itself from **Settings › System › Install**.

Adding `-p:EnableCompressionInSingleFile=true` shrinks the exe to ≈74 MB, but it then uses ≈60 MB more RAM at runtime
(assemblies are decompressed into memory), so the release build ships uncompressed.

---

## 🛠️ Architecture

```
OGConverter/
├── Controls/
│   ├── RadialWheelCanvas.cs      # Hosts a wheel: options, hover/open/switch animation, hands frames to the renderer
│   ├── CropCanvas.cs             # Crop preview: image, dimmed outside, handles, aspect lock
│   └── RangeTimeline.cs          # Trim timeline with start/end handles
├── Wheel/
│   ├── WheelRenderApi.cs         # IWheelRenderer (the plugin adapter), WheelFrame, WheelPalette
│   ├── WheelPainter.cs           # Shared geometry, text, hub helpers for renderers
│   └── Renderers/                # Built-in renderers: Ring, Glass (macOS), Orbit (Dock), Neon
├── Models/
│   └── Models.cs                 # WheelItem, ConversionJob, AppSettings, Protection/Compression options
├── Services/
│   ├── GlobalHookService.cs      # Low-level keyboard & mouse hooks for the drag gesture
│   ├── ShellSelectionService.cs  # Finds the file being dragged in Explorer
│   ├── IdeSelectionService.cs    # Finds the file in a VS Code-based editor's file tree (UI Automation)
│   ├── DroppedFiles.cs           # Reads paths from any drag source (file lists, file:// links, path text)
│   ├── FormatRegistry.cs         # Which conversions, tools and actions each file type gets
│   ├── ConversionEngine.cs       # Runs a wheel choice: FFmpeg, native encoders, documents, tools
│   ├── CompressionService.cs     # Lossless/lossy compression for every file kind
│   ├── ImageCodec.cs             # WIC decode/encode, EXIF orientation, lossless JPEG cleanup
│   ├── PdfCompressor.cs          # PDF image downsampling & stream optimization
│   ├── OfficeCompressor.cs       # DOCX/XLSX/PPTX/ODF picture compression & re-packing
│   ├── MediaToolkit.cs           # FFmpeg runner with progress, media probe, GPU encoder detection
│   ├── MediaEditService.cs       # Crop (images, GIF, video), Trim (video), preview frames
│   ├── DocumentEngine.cs         # Office (COM) / LibreOffice conversions, Markdown
│   ├── BrowserPdfService.cs      # HTML → PDF with headless Edge/Chrome (Markdown/README)
│   ├── PdfToolsService.cs        # Split, merge, rotate, metadata, text, PDF passwords
│   ├── PdfRenderService.cs       # Windows PDF renderer for page images
│   ├── ProtectionService.cs      # Protect/Unlock routing; FileVault.cs (.ogx encryption)
│   ├── SoundService.cs           # Synthesized sound packs per template (or a template's own .wav files)
│   ├── TemplateManager.cs        # Built-in + community templates, import, plugin loading
│   ├── AppThemeService.cs        # Applies a template's palette to the whole app
│   ├── InstallService.cs         # Per-user install/uninstall, start with Windows
│   ├── TrayIconService.cs        # System tray icon
│   ├── SettingsService.cs        # JSON settings in %AppData%\OGConverter
│   └── NotificationService.cs    # Result and progress toasts
├── Views/
│   ├── RadialWheelOverlay.xaml   # Transparent, topmost radial menu
│   ├── CompressDialog.xaml       # Lossless/lossy compression options
│   ├── CropDialog / TrimDialog   # Crop on a preview; pick a video's start and end
│   ├── ProtectDialog / UnlockDialog / PdfSplitDialog / PdfMergeDialog
│   └── ToastNotificationWindow   # Completion & progress toasts
├── MainWindow.xaml               # Home, Settings, About (+ MainWindow.Templates.cs, MainWindow.System.cs)
├── docs/
│   ├── PLUGIN_GUIDE.md           # Beginner's guide: templates, sounds, animations, writing a plugin
│   └── TEMPLATES.md              # Template & plugin quick reference
├── samples/                      # Sample template.json and a sample renderer plugin
└── Tests/                        # --test self-test suite and --snapshot UI renderer
```

---

## 🎯 Usage Guide

1. **Everyday flow**
   - In File Explorer, on your Desktop or in your code editor's file tree, start dragging any file.
   - While holding the mouse button, tap **Shift** (or your mapped key). The wheel appears at your cursor.
   - Move into a slice (you'll hear a tick) and release the mouse button.
   - The result is saved next to the original (or in your chosen output folder).

2. **Compressing**
   - Drag the file, tap **Ctrl**, drop on **Compress**.
   - Choose **Lossless** (identical quality) or **Lossy** (much smaller), pick a preset or fine-tune, then **Compress**.

3. **Cropping and trimming**
   - Drag the image or video, tap **Ctrl**, drop on **Crop** (or **Trim** for a video).
   - Adjust the area or the start and end in the window that opens, then press **Enter**.

4. **Dashboard**
   - Open OG Converter from the tray or taskbar to see history, try the playground, rebind keys and change settings.
   - The ▶ button in the title bar previews the wheel in the middle of the screen.

5. **Make it yours**
   - **Settings › Wheel templates**: pick a template, or add one from the community.
   - **Settings › Wheel › Size**: 40–135%. Small wheels show short labels or icons only.
   - **Settings › Advanced**: hide the shortcut hint bar, or turn on **Reduce motion**.

6. **Install and start with Windows**
   - Click **Install** on the Home banner or in **Settings › System**. OG Converter then starts in the tray every time you sign in (toggle **Start with Windows** there).

---

## 🐧 Contributing

The **Linux version** is the next big step, and work on it starts in a few days. If you want to help (Linux desktop integration, global drag detection on X11/Wayland, packaging, testing), please get in touch: open an issue or reach out through the portfolio link below.

Bug reports, ideas and pull requests for the Windows version are welcome too. See [CONTRIBUTING.md](CONTRIBUTING.md) for how to submit changes.

---

## 👨‍💻 Developers

- **Anandhu Suresh**: [Portfolio](https://dr-at0mic.github.io/)
- **Mahesh P S**
- **Bijo Prasad**

---

## 📄 License

Licensed under the [GNU General Public License v3.0](LICENSE).
