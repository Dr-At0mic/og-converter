using System;

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

        public CompressionOptions Clone() => (CompressionOptions)MemberwiseClone();
    }

    public enum ProtectionMethod
    {
        PdfPassword,     // standard PDF security handler, opens in any PDF reader
        OfficePassword,  // Office "Encrypt with password" (Agile encryption)
        EncryptedFile,   // .ogx container: AEAD cipher + memory-hard key derivation
        PasswordZip      // password-protected .zip
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

        // ZIP
        public ZipCipher ZipCipher { get; set; } = ZipCipher.Aes256;
    }

    public class ConversionJob
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
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
        public bool StartWithWindows { get; set; } = false;
        public double WheelScale { get; set; } = 1.0;
        public bool SoundFeedbackEnabled { get; set; } = true;

        /// <summary>
        /// false (default): the wheel stays open after the shortcut key is let go; drop to choose, Esc to cancel.
        /// true: hold-to-use; letting go of the shortcut key closes the wheel.
        /// </summary>
        public bool CloseWheelOnKeyRelease { get; set; } = false;

        /// <summary>Last choices made in the Compress dialog, keyed by <see cref="CompressionKind"/> name.</summary>
        public System.Collections.Generic.Dictionary<string, CompressionOptions> LastCompression { get; set; } = new();

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
