using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Media;
using OgConverter.Models;

namespace OgConverter.Wheel
{
    /// <summary>
    /// Draws a wheel. The Avalonia counterpart of the Windows app's renderer interface; a template selects one by
    /// <see cref="Id"/>. Only the built-in renderers exist on Linux for now (plugins are WPF assemblies).
    /// </summary>
    /// <remarks>
    /// Render is called on the UI thread for every frame while the wheel animates, and once per change otherwise,
    /// so keep it allocation-light. Everything needed is in <see cref="WheelFrame"/>; <see cref="WheelPainter"/>
    /// has the shared geometry, text and hub helpers the built-in renderers use.
    /// </remarks>
    public interface IWheelRenderer
    {
        /// <summary>Unique, lower-case id referenced by template.json ("renderer": "my-renderer").</summary>
        string Id { get; }

        /// <summary>Shown when a plugin is added without a template.json.</summary>
        string DisplayName { get; }

        void Render(DrawingContext dc, WheelFrame frame);

        /// <summary>Index of the option under <paramref name="position"/>, -1 for none, or null to use the default slice test.</summary>
        int? HitTest(Point position, WheelFrame frame) => null;
    }

    public enum WheelDensity
    {
        /// <summary>Icon, label and description.</summary>
        Full,
        /// <summary>Icon and a smaller label; the hub drops secondary lines.</summary>
        Compact,
        /// <summary>Tiny wheels: short labels (formats) or icons only; the hub shows one line.</summary>
        Micro
    }

    /// <summary>Everything a renderer needs for one frame. Angles are in degrees, 0 = right, clockwise; the first slice starts at the top.</summary>
    public sealed class WheelFrame
    {
        public Point Center { get; internal set; }
        public double OuterRadius { get; internal set; }
        public double InnerRadius { get; internal set; }

        /// <summary>User's size setting (0.4–1.4). Multiply pixel constants by this.</summary>
        public double Scale { get; internal set; }

        /// <summary>Multiplier for font sizes: has a floor so text stays readable on small wheels.</summary>
        public double TextScale { get; internal set; }

        public WheelDensity Density { get; internal set; }

        public IReadOnlyList<WheelItem> Items { get; internal set; } = Array.Empty<WheelItem>();
        public int HoveredIndex { get; internal set; } = -1;

        /// <summary>Animated hover amount per option, 0–1. Use it instead of HoveredIndex for smooth highlights.</summary>
        public IReadOnlyList<double> Hover { get; internal set; } = Array.Empty<double>();

        public WheelMode Mode { get; internal set; }

        /// <summary>Current wheel's accent; animates between wheels when switching.</summary>
        public Color Accent { get; internal set; }

        public WheelPalette Palette { get; internal set; } = null!;
        public WheelTemplate Template { get; internal set; } = null!;

        /// <summary>Extra rotation from the open/switch animation. Already included in <see cref="SliceStart"/>.</summary>
        public double Rotation { get; internal set; }

        public string CenterTitle { get; internal set; } = string.Empty;
        public string CenterSubtitle { get; internal set; } = string.Empty;
        public string CenterGlyph { get; internal set; } = string.Empty;
        public string CenterBadge { get; internal set; } = string.Empty;

        internal Func<int, double>? ItemAlphaFunc;
        internal Func<int, double>? ItemScaleFunc;

        public int Count => Items.Count;
        public double SliceAngle => Items.Count > 0 ? 360.0 / Items.Count : 360.0;

        /// <summary>Start angle of option <paramref name="index"/>.</summary>
        public double SliceStart(int index) => -90.0 + Rotation + index * SliceAngle;

        /// <summary>Middle angle of option <paramref name="index"/>.</summary>
        public double SliceMid(int index) => SliceStart(index) + SliceAngle / 2.0;

        /// <summary>Entrance opacity of an option (staggered during open/switch animations), 0–1.</summary>
        public double ItemAlpha(int index) => ItemAlphaFunc?.Invoke(index) ?? 1.0;

        /// <summary>Entrance scale of an option, around its own center.</summary>
        public double ItemScale(int index) => ItemScaleFunc?.Invoke(index) ?? 1.0;

        public double HoverOf(int index) => index >= 0 && index < Hover.Count ? Hover[index] : 0.0;

        /// <summary>Point at <paramref name="radius"/> along <paramref name="angleDeg"/>.</summary>
        public Point Polar(double radius, double angleDeg)
        {
            double a = angleDeg * Math.PI / 180.0;
            return new Point(Center.X + radius * Math.Cos(a), Center.Y + radius * Math.Sin(a));
        }

        public string LabelOf(int index)
        {
            string t = Items[index].Title;
            return Template.Shape.UppercaseLabels ? t.ToUpperInvariant() : t;
        }
    }

    /// <summary>A template's wheel colors, parsed once, with immutable brushes ready to draw.</summary>
    public sealed class WheelPalette
    {
        public Color Ring { get; }
        public Color Hub { get; }
        public Color Text { get; }
        public Color TextMuted { get; }
        public Color TextFaint { get; }
        public Color Edge { get; }
        public Color Divider { get; }
        public Color Shadow { get; }
        public Color Convert { get; }
        public Color Tools { get; }
        public Color Actions { get; }

        /// <summary>Text on a filled accent: the template's choice, else white or near-black by contrast.</summary>
        public Color OnAccentFor(Color accent) => _onAccent ?? (ColorUtil.Luminance(accent) > 0.62 ? Color.FromRgb(0x14, 0x14, 0x18) : Colors.White);

        public IBrush RingBrush { get; }
        public IBrush HubBrush { get; }
        public IBrush TextBrush { get; }
        public IBrush TextMutedBrush { get; }
        public IBrush TextFaintBrush { get; }
        public IPen EdgePen { get; }
        public IPen DividerPen { get; }

        /// <summary>True when the wheel surface is light (dark text).</summary>
        public bool IsLight { get; }

        private readonly Color? _onAccent;

        public WheelPalette(WheelColors c)
        {
            var d = new WheelColors();
            Ring = ColorUtil.Parse(c.Ring, ColorUtil.Parse(d.Ring, Colors.Black));
            Hub = ColorUtil.Parse(c.Hub, ColorUtil.Parse(d.Hub, Colors.Black));
            Text = ColorUtil.Parse(c.Text, Colors.White);
            TextMuted = ColorUtil.Parse(c.TextMuted, ColorUtil.Parse(d.TextMuted, Colors.Gray));
            TextFaint = ColorUtil.Parse(c.TextFaint, ColorUtil.Parse(d.TextFaint, Colors.Gray));
            Edge = ColorUtil.Parse(c.Edge, ColorUtil.Parse(d.Edge, Colors.Transparent));
            Divider = ColorUtil.Parse(c.Divider, ColorUtil.Parse(d.Divider, Colors.Transparent));
            Shadow = ColorUtil.Parse(c.Shadow, ColorUtil.Parse(d.Shadow, Colors.Transparent));
            Convert = ColorUtil.Parse(c.Convert, ColorUtil.Parse(d.Convert, Colors.Orange));
            Tools = ColorUtil.Parse(c.Tools, ColorUtil.Parse(d.Tools, Colors.DodgerBlue));
            Actions = ColorUtil.Parse(c.Actions, ColorUtil.Parse(d.Actions, Colors.MediumPurple));
            _onAccent = string.IsNullOrWhiteSpace(c.OnAccent) ? null : ColorUtil.Parse(c.OnAccent, Colors.White);
            IsLight = ColorUtil.Luminance(Ring) > 0.5;

            RingBrush = WheelPainter.Solid(Ring);
            HubBrush = WheelPainter.Solid(Hub);
            TextBrush = WheelPainter.Solid(Text);
            TextMutedBrush = WheelPainter.Solid(TextMuted);
            TextFaintBrush = WheelPainter.Solid(TextFaint);
            EdgePen = WheelPainter.Pen(Edge, 1);
            DividerPen = WheelPainter.Pen(Divider, 1);
        }

        public Color AccentFor(WheelMode mode) => mode switch
        {
            WheelMode.Tools => Tools,
            WheelMode.Actions => Actions,
            _ => Convert
        };

        public static WheelPalette For(WheelTemplate template)
        {
            if (template.ResolvedCache is WheelPalette p) return p;
            p = new WheelPalette(template.Wheel);
            template.ResolvedCache = p;
            return p;
        }
    }

    public static class ColorUtil
    {
        public static Color Parse(string? value, Color fallback) => TryParse(value, out var c) ? c : fallback;

        public static bool TryParse(string? value, out Color color)
        {
            color = default;
            return !string.IsNullOrWhiteSpace(value) && Color.TryParse(value.Trim(), out color);
        }

        public static Color WithAlpha(Color c, byte alpha) => Color.FromArgb(alpha, c.R, c.G, c.B);

        public static Color WithAlpha(Color c, double alpha) => Color.FromArgb((byte)Math.Clamp(alpha * 255.0, 0, 255), c.R, c.G, c.B);

        /// <summary>Linear blend: t = 0 gives <paramref name="a"/>, t = 1 gives <paramref name="b"/>.</summary>
        public static Color Lerp(Color a, Color b, double t)
        {
            t = Math.Clamp(t, 0, 1);
            return Color.FromArgb(
                (byte)(a.A + (b.A - a.A) * t),
                (byte)(a.R + (b.R - a.R) * t),
                (byte)(a.G + (b.G - a.G) * t),
                (byte)(a.B + (b.B - a.B) * t));
        }

        /// <summary>Blend toward <paramref name="toward"/> keeping <paramref name="c"/>'s alpha.</summary>
        public static Color Mix(Color c, Color toward, double t)
        {
            var m = Lerp(c, toward, t);
            return Color.FromArgb(c.A, m.R, m.G, m.B);
        }

        /// <summary>Relative luminance, 0 (black) – 1 (white).</summary>
        public static double Luminance(Color c)
        {
            static double Ch(byte v)
            {
                double s = v / 255.0;
                return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
            }
            return 0.2126 * Ch(c.R) + 0.7152 * Ch(c.G) + 0.0722 * Ch(c.B);
        }

        public static string ToHex(Color c) => c.A == 0xFF ? $"#{c.R:X2}{c.G:X2}{c.B:X2}" : $"#{c.A:X2}{c.R:X2}{c.G:X2}{c.B:X2}";
    }
}
