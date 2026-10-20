using System.Collections.Generic;
using System.Text;
using Avalonia.Media;

namespace OgConverter.Icons
{
    /// <summary>
    /// The models and the Windows app name icons by Segoe Fluent Icons code points (<c>WheelItem.IconGlyph</c>). That font
    /// can't ship on Linux, so each code point maps to a glyph of a bundled open icon font: a subset of Microsoft's
    /// Fluent UI System Icons (MIT, Assets/Fonts/FluentSystemIcons-Regular.LICENSE.txt), the same design language.
    /// </summary>
    public static class Icons
    {
        /// <summary>The bundled icon font.</summary>
        public static FontFamily Font { get; } = new("avares://og-converter/Assets/Fonts#FluentSystemIcons-Regular");

        /// <summary>Shown for a Segoe code point that has no mapping (e.g. from a plugin): a blank page.</summary>
        private const int Fallback = 0xF379;

        /// <summary>
        /// Every Segoe Fluent Icons code point used in Core and the Windows app → Fluent UI System Icons code point
        /// (the 24 px regular icon unless noted). Adding one means adding it to the bundled subset font too.
        /// </summary>
        private static readonly Dictionary<char, int> Map = new()
        {
            [''] = 0xF56C,  // QuickNote → note
            [''] = 0xF2A4,  // ChevronDown → chevron_down
            [''] = 0xF10A,  // Add → add
            [''] = 0xF36A,  // Cancel → dismiss
            [''] = 0xF6AA,  // Setting → settings
            [''] = 0xF84D,  // Video → video
            [''] = 0xF4E5,  // Link → link
            [''] = 0xF255,  // Camera → camera
            [''] = 0xF13E,  // Refresh → arrow_clockwise
            [''] = 0xE790,  // Lock → lock_closed
            [''] = 0xF710,  // FavoriteStar → star
            [''] = 0xF295,  // CheckMark → checkmark
            [''] = 0xF166,  // BackToWindow (Compress) → arrow_minimize
            [''] = 0xF160,  // FullScreen → arrow_maximize
            [''] = 0xF62B,  // Print → print
            [''] = 0xF19C,  // Up → arrow_up
            [''] = 0xF149,  // Down → arrow_down
            [''] = 0xF34D,  // Delete → delete
            [''] = 0xEB4B,  // Mute → speaker_mute
            [''] = 0xEE6F,  // CommandPrompt → window_console (20 px)
            [''] = 0xF4B9,  // KeyboardClassic → keyboard
            [''] = 0xEB43,  // Volume → speaker_2
            [''] = 0xF606,  // Play → play
            [''] = 0xF2B1,  // ChevronRight → chevron_right
            [''] = 0xF45B,  // Globe → globe
            [''] = 0xF3F2,  // Error → error_circle
            [''] = 0xE796,  // Unlock → lock_open
            [''] = 0xF6EC,  // SlideShow → slide_text
            [''] = 0xF2F6,  // Color → color
            [''] = 0xF331,  // Crop → crop
            [''] = 0xF186,  // Rotate → arrow_rotate_clockwise
            [''] = 0xE1BC,  // Package → box
            [''] = 0xF86A,  // Warning → warning
            [''] = 0xF379,  // Page → document
            [''] = 0xF60F,  // PowerButton → power
            [''] = 0xF35A,  // TVMonitor → desktop
            [''] = 0xF472,  // Headphone → headphones
            [''] = 0xF75E,  // ViewAll (spreadsheets) → table
            [''] = 0xF481,  // Home → home
            [''] = 0xF47F,  // History → history
            [''] = 0xF42F,  // FolderOpen → folder_open
            [''] = 0xF191,  // Sync → arrow_sync
            [''] = 0xF151,  // Download → arrow_download
            [''] = 0xF1A5,  // Upload → arrow_upload
            [''] = 0xF166,  // PreviewLink (hide to tray) → arrow_minimize
            [''] = 0xE558,  // Document → document_text
            [''] = 0xF583,  // OpenInNewWindow → open
            [''] = 0xF18E,  // Switch → arrow_swap
            [''] = 0xF419,  // Folder → folder
            [''] = 0xF489,  // Picture → image
            [''] = 0xF36A,  // ChromeClose → dismiss
            [''] = 0xF33B,  // Cut → cut
            [''] = 0xF32C,  // Copy → copy
            [''] = 0xF7E5,  // Font (text) → text_font
            [''] = 0xE855,  // Audio → music_note_2
            [''] = 0xF4B7,  // Permissions → key
            [''] = 0xF0C60, // OpenFile → document_arrow_up
            [''] = 0xF4D3,  // Library → library
            [''] = 0xF8C1,  // Repair → wrench
            [''] = 0xF827,  // Stopwatch → timer
            [''] = 0xF489,  // Photo → image
            [''] = 0xEBD1,  // ChromeMinimize → subtract
            [''] = 0xE7EC,  // ChromeMaximize → maximize
            [''] = 0xF78C,  // ChromeRestore → square_multiple
            [''] = 0xF4BF,  // (shortcut hints setting) → keyboard_shift
            [''] = 0xF2F0,  // Code → code
            [''] = 0xE619,  // LightningBolt → flash
            [''] = 0xF4A4,  // Info → info
            [''] = 0xF35C,  // Processing (CPU) → developer_board
            [''] = 0xF6BF,  // Shield → shield
            [''] = 0xF115,  // Ringer → alert
            [''] = 0xF3AD,  // PDF → document_pdf
            [''] = 0xF489,  // Photo2 → image
            [''] = 0xE0C8,  // Export → arrow_export_ltr
        };

        private static readonly Dictionary<string, string> Cache = new();

        /// <summary>
        /// The bundled font's glyph for a Segoe Fluent Icons code point (e.g. ""). Characters outside the private use
        /// area pass through unchanged; unknown icons become a blank page. Not idempotent: map each Segoe string once.
        /// </summary>
        public static string Glyph(string segoeGlyph)
        {
            if (string.IsNullOrEmpty(segoeGlyph)) return string.Empty;
            lock (Cache)
            {
                if (Cache.TryGetValue(segoeGlyph, out var cached)) return cached;

                var sb = new StringBuilder(segoeGlyph.Length * 2);
                foreach (char c in segoeGlyph)
                {
                    if (c is < '' or > '') sb.Append(c);
                    else sb.Append(char.ConvertFromUtf32(Map.TryGetValue(c, out int cp) ? cp : Fallback));
                }
                string mapped = sb.ToString();
                Cache[segoeGlyph] = mapped;
                return mapped;
            }
        }
    }
}
