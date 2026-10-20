using System;
using System.Globalization;
using Avalonia.Markup.Xaml;

namespace OgConverter.Controls
{
    /// <summary>
    /// <c>{c:Icon E713}</c>: the glyph for a Segoe Fluent Icons code point, as the bundled icon font draws it
    /// (<see cref="Icons.Icons.Glyph"/>). Lets the XAML keep the same code points as the Windows app.
    /// </summary>
    public sealed class IconExtension : MarkupExtension
    {
        public IconExtension(string code)
        {
            Code = code;
        }

        /// <summary>Hex code point, e.g. "E713".</summary>
        public string Code { get; set; }

        public override object ProvideValue(IServiceProvider serviceProvider) => Glyph(Code);

        /// <summary>The glyph for a hex code point ("E713").</summary>
        public static string Glyph(string hex) =>
            Icons.Icons.Glyph(char.ConvertFromUtf32(int.Parse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture)));
    }
}
