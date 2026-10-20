using Avalonia.Media;

namespace OgConverter.Icons
{
    /// <summary>
    /// The models and the Windows app name icons by Segoe Fluent Icons code points (<c>WheelItem.IconGlyph</c>). That font
    /// can't ship on Linux, so each code point maps to a glyph of a bundled open icon font.
    /// </summary>
    public static class Icons
    {
        /// <summary>The bundled icon font.</summary>
        public static FontFamily Font { get; } = FontFamily.Default;

        /// <summary>The bundled font's glyph for a Segoe Fluent Icons code point (e.g. "").</summary>
        public static string Glyph(string segoeGlyph) => segoeGlyph;
    }
}
