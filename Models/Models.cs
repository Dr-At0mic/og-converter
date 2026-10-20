using System;
using System.ComponentModel;

namespace OgConverter.Models
{
    public enum WheelMode
    {
        Convert,
        Tools,
        Actions
    }

    public enum FileCategory
    {
        Image,
        Video,
        Audio,
        Document,
        Spreadsheet,
        Presentation,
        Archive,
        Other
    }

    public class WheelItem
    {
        public string Id { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Extension { get; set; } = string.Empty;
        public string IconGlyph { get; set; } = "\uE7C3"; // Segoe Fluent Icons code point
        public WheelMode Mode { get; set; } = WheelMode.Convert;
        public string Category { get; set; } = "General";
        public bool IsHighlighted { get; set; }
    }

    public enum PdfSplitMode
    {
        ExtractToOne,   // selected pages → one PDF
        EachRange,      // each range → its own PDF
        EveryPage       // every page → its own PDF
    }

    /// <summary>User input collected by a dialog for tools that need more than a single click.</summary>
    public class ToolParameters
    {
        public string PageRanges { get; set; } = string.Empty;
        public PdfSplitMode SplitMode { get; set; } = PdfSplitMode.ExtractToOne;
        public System.Collections.Generic.List<string> MergeInputs { get; set; } = new();

        /// <summary>Current password, for Unlock.</summary>
        public string Password { get; set; } = string.Empty;

        /// <summary>Method, password and encryption choices, for Protect.</summary>
        public ProtectionOptions? Protection { get; set; }

        /// <summary>Mode, format and quality choices, for Compress.</summary>
        public CompressionOptions? Compression { get; set; }

        /// <summary>Format, level and password choices, for the Archive action.</summary>
        public ArchiveOptions? Archive { get; set; }

        /// <summary>Area to keep, in pixels of the upright image or video frame, for Crop.</summary>
        public System.Windows.Int32Rect? Crop { get; set; }

        /// <summary>Part of the video to keep, in seconds, for Trim.</summary>
        public double TrimStart { get; set; }
        public double TrimEnd { get; set; }
        /// <summary>Copy the streams instead of re-encoding: instant, but the cut snaps to the nearest keyframe.</summary>
        public bool TrimFast { get; set; }
    }

    /// <summary>What kind of compression pipeline a file goes through.</summary>
    public enum CompressionKind { Image, AnimatedGif, Svg, Video, Audio, Pdf, Office, Archive, General }

    public enum CompressionMode { Lossless, Lossy }

    /// <summary>
    /// Every choice the Compress dialog offers. One flat bag shared by all file kinds; each pipeline reads the
    /// fields that apply to it. Persisted per kind so the dialog reopens with the last-used choices.
    /// </summary>
    public class CompressionOptions
    {
        public CompressionMode Mode { get; set; } = CompressionMode.Lossy;

        /// <summary>
        /// Output format key. Images: keep, jpg, png, webp, avif, jxl, tiff. GIF: gif, webp.
        /// Video lossless: remux, ffv1, x264, x265. Video lossy: h264, h265, av1, vp9.
        /// Audio: keep, flac, alac, opus, aac, mp3. General: zip, gz, br. Svg: min, svgz.
        /// </summary>
        public string Format { get; set; } = "keep";

        /// <summary>1–100. Mapped to each encoder's own scale (JPEG quality, CRF, Butteraugli distance…).</summary>
        public int Quality { get; set; } = 80;

        /// <summary>Longest edge in pixels for images and pictures inside documents. 0 = keep.</summary>
        public int MaxDimension { get; set; }

        public bool StripMetadata { get; set; } = true;

        // Video
        /// <summary>0 = use <see cref="Quality"/>; otherwise aim for this file size (two-pass where supported).</summary>
        public double TargetSizeMb { get; set; }
        /// <summary>Short-edge resolution cap (720 = 720p). 0 = keep.</summary>
        public int MaxHeight { get; set; }
        /// <summary>Frame-rate cap. 0 = keep.</summary>
        public int MaxFps { get; set; }
        /// <summary>0 fastest … 3 smallest file (slowest).</summary>
        public int Speed { get; set; } = 1;
        public bool UseHardwareEncoder { get; set; }
        /// <summary>-1 = remove audio, 0 = keep the original track, otherwise re-encode at this many kbps.</summary>
        public int VideoAudioKbps { get; set; } = 128;

        // Audio
        public int AudioKbps { get; set; } = 128;
        public bool Mono { get; set; }
        /// <summary>0 = keep.</summary>
        public int SampleRate { get; set; }

        // PDF
        /// <summary>Images are downsampled so they never exceed this resolution at the page's size.</summary>
        public int PdfDpi { get; set; } = 150;
        public bool Grayscale { get; set; }

        // Guetzli
        /// <summary>Encode even if the picture needs more memory than is free.</summary>
        public bool GuetzliAllowLarge { get; set; }

        public CompressionOptions Clone() => (CompressionOptions)MemberwiseClone();
    }

    public enum ProtectionMethod
    {
        PdfPassword,     // standard PDF security handler, opens in any PDF reader
        OfficePassword,  // Office "Encrypt with password" (Agile encryption)
        EncryptedFile,   // .ogx container: AEAD cipher + memory-hard key derivation
        PasswordArchive  // password-protected .zip, .7z or .rar
    }

    public enum PdfCipher { Aes256, Aes128, Rc4_128 }
    public enum FileCipher { Aes256Gcm, ChaCha20Poly1305 }
    public enum KeyDerivation { Argon2id, Pbkdf2Sha512 }
    public enum ZipCipher { Aes256, ZipCrypto }

    public class ProtectionOptions
    {
        public ProtectionMethod Method { get; set; }
        public string Password { get; set; } = string.Empty;

        // PDF
        public PdfCipher PdfCipher { get; set; } = PdfCipher.Aes256;
        public bool AllowPrinting { get; set; } = true;
        public bool AllowCopying { get; set; } = true;
        public bool AllowEditing { get; set; } = true;
        public bool AllowComments { get; set; } = true;
        /// <summary>Password that unlocks the restricted permissions. Blank = random (restrictions can't be lifted).</summary>
        public string PermissionsPassword { get; set; } = string.Empty;

        // Office
        /// <summary>Optional second password required to save changes (anyone with the open password can still read).</summary>
        public string ModifyPassword { get; set; } = string.Empty;

        // Encrypted file (.ogx)
        public FileCipher FileCipher { get; set; } = FileCipher.Aes256Gcm;
        public KeyDerivation KeyDerivation { get; set; } = KeyDerivation.Argon2id;

        // Password-protected archive
        /// <summary>"zip", "7z" or "rar".</summary>
        public string ArchiveFormat { get; set; } = "zip";
        public ZipCipher ZipCipher { get; set; } = ZipCipher.Aes256;
        /// <summary>7z and RAR: hide the file names too, so nothing is visible without the password.</summary>
        public bool EncryptNames { get; set; } = true;
        /// <summary>Native compression level of <see cref="ArchiveFormat"/>; -1 = its default.</summary>
        public int ArchiveLevel { get; set; } = -1;
    }

    /// <summary>What happens to pictures inside a folder before it is packed.</summary>
    public enum ImageRecompression
    {
        None,
        /// <summary>JPEG metadata removed, PNGs re-encoded; pixels unchanged.</summary>
        Lossless,
        /// <summary>JPEGs re-encoded with Google's Guetzli: visually identical, 20–30% smaller, very slow.</summary>
        Guetzli
    }

    /// <summary>Every choice the Archive dialog offers. Each format reads the fields that apply to it.</summary>
    public class ArchiveOptions
    {
        /// <summary>Format key from <c>ArchiveWriter.Formats</c>: zip, 7z, rar, tar, tar.gz, tar.zst, gz, zst, cpio, iso…</summary>
        public string Format { get; set; } = "zip";

        /// <summary>The format's native level (zip 0–9, zstd 1–22, Brotli 0–11, RAR 0–5…); -1 = its default.</summary>
        public int Level { get; set; } = -1;

        /// <summary>ZIP: deflate, store. 7z: lzma2, lzma, ppmd, bzip2, deflate, store. Blank = the format's default.</summary>
        public string Method { get; set; } = "";

        /// <summary>7z and RAR: compress all files as one stream (smaller, but one damaged byte affects everything after it).</summary>
        public bool Solid { get; set; } = true;

        /// <summary>Use every CPU core where the compressor supports it (7z, RAR, xz, zstd).</summary>
        public bool MultiThreaded { get; set; } = true;

        /// <summary>zstd: long-distance matching with a 128 MiB window (much smaller for big, repetitive data).</summary>
        public bool LongRange { get; set; }

        /// <summary>Brotli: log2 of the window size, 10–24.</summary>
        public int BrotliWindow { get; set; } = 22;

        /// <summary>RAR: recovery record as a percentage of the archive size, 0 = none.</summary>
        public int RecoveryPercent { get; set; }

        /// <summary>Blank = no password. Only ZIP, 7z and RAR can hold one. Never saved to settings.</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public string Password { get; set; } = string.Empty;

        public ZipCipher ZipCipher { get; set; } = ZipCipher.Aes256;

        /// <summary>7z and RAR: encrypt the file names too.</summary>
        public bool EncryptNames { get; set; } = true;

        public ImageRecompression Images { get; set; } = ImageRecompression.None;

        /// <summary>Guetzli quality, 84–100 (Guetzli refuses lower values).</summary>
        public int GuetzliQuality { get; set; } = 90;

        /// <summary>Pictures Guetzli encodes at the same time; 0 = as many as the cores and free memory allow.</summary>
        public int GuetzliJobs { get; set; }

        /// <summary>Also encode pictures that need more memory than is free (Windows pages to disk; slow, but it works).</summary>
        public bool GuetzliAllowLarge { get; set; }

        public ArchiveOptions Clone() => (ArchiveOptions)MemberwiseClone();
    }

    public class ConversionJob : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        public string Id { get; set; } = Guid.NewGuid().ToString("N");

        /// <summary>Placeholder shown in Recent activity while the work runs; replaced by the finished job.</summary>
        public bool IsRunning { get; init; }

        private double _progress;
        /// <summary>0–1 while running. Only meaningful once <see cref="HasProgress"/> is true.</summary>
        public double Progress
        {
            get => _progress;
            set
            {
                _progress = Math.Clamp(value, 0, 1);
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Progress)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ProgressText)));
                if (!HasProgress)
                {
                    HasProgress = true;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasProgress)));
                }
            }
        }

        /// <summary>False until the first report: the work can't be measured (yet), so show activity instead of a percentage.</summary>
        public bool HasProgress { get; private set; }

        public string ProgressText => HasProgress ? $"{Progress * 100:0}%" : "Working…";
        public ToolParameters? Parameters { get; set; }
        public string SourcePath { get; set; } = string.Empty;
        public string TargetPath { get; set; } = string.Empty;
        public string TargetFormat { get; set; } = string.Empty;
        public WheelItem Option { get; set; } = new();
        public DateTime StartTime { get; set; } = DateTime.Now;
        public DateTime? EndTime { get; set; }
        public bool IsSuccess { get; set; }
        public string? ErrorMessage { get; set; }
        public long OriginalSizeBytes { get; set; }
        public long ConvertedSizeBytes { get; set; }

        /// <summary>Value produced instead of a file (e.g. a checksum); shown in place of the size comparison.</summary>
        public string? ResultText { get; set; }

        public string DetailText => !string.IsNullOrEmpty(ResultText) ? $"Copied {ResultText}" : SizeComparisonText;

        public string SourceFileName => System.IO.Path.GetFileName(SourcePath);
        public string TargetFileName => System.IO.Path.GetFileName(TargetPath);

        public string DurationText => EndTime.HasValue 
            ? $"{(EndTime.Value - StartTime).TotalSeconds:0.0}s" 
            : "...";

        public string SizeComparisonText
        {
            get
            {
                if (ConvertedSizeBytes <= 0 || OriginalSizeBytes <= 0) return "";
                double diff = ((double)ConvertedSizeBytes - OriginalSizeBytes) / OriginalSizeBytes * 100.0;
                string sign = diff > 0 ? "+" : "";
                return $"{FormatSize(OriginalSizeBytes)} → {FormatSize(ConvertedSizeBytes)} ({sign}{diff:0.0}%)";
            }
        }

        private static string FormatSize(long bytes)
        {
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024.0:0.0} KB";
            if (bytes < 1024 * 1024 * 1024) return $"{bytes / (1024.0 * 1024.0):0.0} MB";
            return $"{bytes / (1024.0 * 1024.0 * 1024.0):0.0} GB";
        }
    }

    public class AppSettings
    {
        public bool SaveInSameDirectory { get; set; } = true;
        public string CustomOutputDirectory { get; set; } = string.Empty;
        public bool OverwriteExisting { get; set; } = false;
        public string ConvertKey { get; set; } = "Shift";
        public string ToolsKey { get; set; } = "Control";
        public string ActionsKey { get; set; } = "Alt";
        public int ImageQuality { get; set; } = 85;
        public int VideoCrf { get; set; } = 24; // 18-28 typical
        public bool PlaySoundOnComplete { get; set; } = false;
        public bool ShowToastOnComplete { get; set; } = true;
        /// <summary>Launch at sign-in, straight to the tray. Mirrors the HKCU Run entry, which is the source of truth.</summary>
        public bool StartWithWindows { get; set; } = false;
        public double WheelScale { get; set; } = 0.45;
        public bool SoundFeedbackEnabled { get; set; } = true;

        /// <summary>Id of the active wheel template (see TemplateManager).</summary>
        public string WheelTemplateId { get; set; } = "og-classic";

        /// <summary>The Shift / Ctrl / Alt / Esc bar under the wheel.</summary>
        public bool ShowKeyHints { get; set; } = false;

        /// <summary>Skip wheel animations.</summary>
        public bool ReduceMotion { get; set; } = false;

        /// <summary>The "Running without installing" banner on Home was dismissed.</summary>
        public bool HideInstallBanner { get; set; } = false;

        /// <summary>The "Recommended extras" prompt (FFmpeg, LibreOffice) was shown once; Settings › Engine has the same buttons.</summary>
        public bool ExtrasPromptShown { get; set; } = false;

        /// <summary>
        /// false (default): the wheel stays open after the shortcut key is let go; drop to choose, Esc to cancel.
        /// true: hold-to-use; letting go of the shortcut key closes the wheel.
        /// </summary>
        public bool CloseWheelOnKeyRelease { get; set; } = false;

        /// <summary>Last choices made in the Compress dialog, keyed by <see cref="CompressionKind"/> name.</summary>
        public System.Collections.Generic.Dictionary<string, CompressionOptions> LastCompression { get; set; } = new();

        /// <summary>Last choices made in the Archive dialog (the password is never saved).</summary>
        public ArchiveOptions? LastArchive { get; set; }

        public static readonly string[] AvailableWheelKeys = { "Shift", "Control", "Alt", "Win", "Space" };

        /// <summary>
        /// Each wheel needs its own trigger key; a shared key makes all but the first wheel unreachable.
        /// Repairs invalid or duplicate bindings (e.g. from a hand-edited settings file) in place.
        /// Returns true if anything was changed.
        /// </summary>
        public bool EnsureUniqueKeyBindings()
        {
            var used = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);

            string Resolve(string key, string fallback)
            {
                string? valid = Array.Find(AvailableWheelKeys, k => string.Equals(k, key, StringComparison.OrdinalIgnoreCase));
                if (valid != null && used.Add(valid)) return valid;
                if (used.Add(fallback)) return fallback;
                string free = Array.Find(AvailableWheelKeys, k => !used.Contains(k))!;
                used.Add(free);
                return free;
            }

            string convert = Resolve(ConvertKey, "Shift");
            string tools = Resolve(ToolsKey, "Control");
            string actions = Resolve(ActionsKey, "Alt");

            bool changed = convert != ConvertKey || tools != ToolsKey || actions != ActionsKey;
            ConvertKey = convert;
            ToolsKey = tools;
            ActionsKey = actions;
            return changed;
        }
    }
}
