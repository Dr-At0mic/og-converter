# OG Converter ⚡
> The zero-click offline file converter for Windows (inspired by Tangerine for Mac).

*Powered by pure boredom, caffeine, and living in a world with no superpowers, no magic, and no zombie apocalypse to save us from desk jobs.*

Drag a file anywhere in Windows, tap your configured hotkey (e.g. **Shift**), and drop. Turn files into the formats you need without breaking your flow. Everything happens 100% locally on your PC.

![OG Converter home screen](Assets/public/og-home.png)

> **Platform support:** this version runs on **Windows only** (Windows 10 1809+ / Windows 11).
> A **Linux version is coming soon**: work starts in a few days. If you'd like to help build it, please reach out (see [Contributing](#-contributing)).

> **Project status:** this is **not a fully optimized release** yet. What it does aim for is staying light while it waits in the background.
> Measured on Windows 11 with the self-contained release build, sitting idle with the dashboard open:
> **~75–80 MB private memory (~120 MB working set)**. Heavy jobs (video, audio, GIF, WebP/AVIF/JPEG XL) run in a separate
> FFmpeg process, which uses extra memory only while it's working and exits when done.
>
> Found a bug, or hit any difficulty while using it? Please **[open an issue](../../issues)** describing what happened.
> I'll try to resolve it as soon as I get time.

---

## ✨ Key Features

- **Zero-Click Drag & Drop**: Drag a file from File Explorer or the Desktop, tap a modifier key, and the radial wheel appears right at your cursor.
- **Three wheels, rebindable keys** (**Shift**, **Ctrl**, **Alt**, **Win**, **Space**):
  - `⇧ Shift`: **Convert**: images, video, audio, PDF, Word/Excel/PowerPoint, OpenDocument, Markdown, archives.
  - `Ctrl`: **Tools**: Compress, resize, grayscale, remove EXIF, mute, 720p, PDF split/merge/rotate/protect, and more.
  - `Alt`: **Actions**: Copy Path, Show in Explorer, Terminal, SHA-256, Duplicate, Zip, Base64, Protect/Unlock.
  - `Esc`: cancel the wheel at any time.
- **Lossless & lossy compression for every file type** (see below).
- **Password protection**: PDF (AES-256), Office (Agile AES-256), encrypted `.ogx` files (AES-256-GCM / ChaCha20-Poly1305 + Argon2id), password ZIPs.
- **Settings**: output folder, wheel size, sound feedback, overwrite behaviour, toasts, start with Windows.
- **100% Offline & Private**: no uploads, no telemetry, no account.
- **System Tray**: sits quietly in the notification area (≈75–80 MB private memory when idle).

---

## ⌨️ Shortcuts

Start dragging a file in File Explorer, then tap a key while still holding the mouse button. The wheel opens at your cursor; drop on a slice to run it. Each key can be rebound on the Home screen.

### `Shift`: Convert
Change the file's format. For a PDF: Word, PNG, JPG, WebP, TIFF, or plain text.

![Convert wheel (Shift) on a PDF](Assets/public/og-short-cut-convert.png)

### `Ctrl`: Tools
Edit or optimize the file. For a PDF: Compress, Split / Extract, Merge, Rotate, Cover Image, Remove Metadata, Protect.

![Tools wheel (Ctrl) on a PDF](Assets/public/og-short-cut-tools.png)

### `Alt`: Actions
Quick file actions: Copy Path, Show in Explorer, Terminal, SHA-256, Duplicate, Zip, Base64, Protect.

![Actions wheel (Alt) on a PDF](Assets/public/og-short-cut-actions.png)

Press `Esc` at any time to close the wheel.

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

## 🚀 How to Run

### Prerequisites
- Windows 10 (1809) or later
- [.NET 10 SDK](https://dotnet.microsoft.com/) (for building)
- [FFmpeg](https://ffmpeg.org/) on `PATH` or next to the exe: `winget install Gyan.FFmpeg`
- Optional, for Office conversions: Microsoft Office, or LibreOffice (`winget install TheDocumentFoundation.LibreOffice`)

### Run in Development
```powershell
# In e:\Projects\og-converter
dotnet run
```

### Run the Self-Test Suite
```powershell
dotnet run -- --test
```
Covers conversions, PDF tools, protection, and compression for every file type and mode: lossless results are checked pixel-for-pixel, target sizes are measured, and PDFs and Office files are validated after compression.

### Publish a Release Build (single self-contained `.exe`)
```powershell
dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```
Output: `bin\Release\net10.0-windows10.0.19041.0\win-x64\publish\og-converter.exe` (≈174 MB, no .NET install needed).

Adding `-p:EnableCompressionInSingleFile=true` shrinks the exe to ≈74 MB, but it then uses ≈60 MB more RAM at runtime
(assemblies are decompressed into memory), so the release build ships uncompressed.

---

## 🛠️ Architecture

```
OGConverter/
├── Controls/
│   └── RadialWheelCanvas.cs      # Vector radial wheel with scaling & haptics
├── Models/
│   └── Models.cs                 # WheelItem, ConversionJob, AppSettings, Protection/Compression options
├── Services/
│   ├── GlobalHookService.cs      # Low-level keyboard & mouse hooks for the drag gesture
│   ├── ShellSelectionService.cs  # Finds the file being dragged in Explorer
│   ├── FormatRegistry.cs         # Which conversions, tools and actions each file type gets
│   ├── ConversionEngine.cs       # Runs a wheel choice: FFmpeg, native encoders, documents, tools
│   ├── CompressionService.cs     # Lossless/lossy compression for every file kind
│   ├── ImageCodec.cs             # WIC decode/encode, EXIF orientation, lossless JPEG cleanup
│   ├── PdfCompressor.cs          # PDF image downsampling & stream optimization
│   ├── OfficeCompressor.cs       # DOCX/XLSX/PPTX/ODF picture compression & re-packing
│   ├── MediaToolkit.cs           # FFmpeg runner with progress, media probe, GPU encoder detection
│   ├── DocumentEngine.cs         # Office (COM) / LibreOffice conversions, Markdown
│   ├── PdfToolsService.cs        # Split, merge, rotate, metadata, text, PDF passwords
│   ├── PdfRenderService.cs       # Windows PDF renderer for page images
│   ├── ProtectionService.cs      # Protect/Unlock routing; FileVault.cs (.ogx encryption)
│   ├── SoundService.cs           # Synthesized tactile audio ticks
│   ├── TrayIconService.cs        # System tray icon
│   ├── SettingsService.cs        # JSON settings in %AppData%\OGConverter
│   └── NotificationService.cs    # Result and progress toasts
├── Views/
│   ├── RadialWheelOverlay.xaml   # Transparent, topmost radial menu
│   ├── CompressDialog.xaml       # Lossless/lossy compression options
│   ├── ProtectDialog / UnlockDialog / PdfSplitDialog / PdfMergeDialog
│   └── ToastNotificationWindow   # Completion & progress toasts
├── MainWindow.xaml               # Home, Settings, About
└── Tests/                        # --test self-test suite and --snapshot UI renderer
```

---

## 🎯 Usage Guide

1. **Everyday flow**
   - In File Explorer or on your Desktop, start dragging any file.
   - While holding the mouse button, tap **Shift** (or your mapped key). The wheel appears at your cursor.
   - Move into a slice (you'll hear a tick) and release the mouse button.
   - The result is saved next to the original (or in your chosen output folder).

2. **Compressing**
   - Drag the file, tap **Ctrl**, drop on **Compress**.
   - Choose **Lossless** (identical quality) or **Lossy** (much smaller), pick a preset or fine-tune, then **Compress**.

3. **Dashboard**
   - Open OG Converter from the tray or taskbar to see history, try the playground, rebind keys and change settings.

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
