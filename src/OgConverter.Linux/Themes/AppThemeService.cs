using System;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Styling;
using OgConverter.Models;

namespace OgConverter.Themes
{
    /// <summary>
    /// Pushes a template's app palette into the Application resources. Every themed color in the XAML is a
    /// DynamicResource, so replacing these entries restyles open windows immediately. Port of the Windows AppThemeService.
    /// </summary>
    public static class AppThemeService
    {
        private sealed record Base(
            string Window, string Surface, string SurfaceRaised, string SurfaceHover, string SurfacePressed, string Input,
            string Border, string BorderStrong, string Divider, string TextPrimary, string TextSecondary, string TextTertiary,
            string Success, string Warning, string Danger);

        private static readonly Base Dark = new(
            "#0D0E12", "#15171C", "#1C1E24", "#23262D", "#2A2D35", "#101216",
            "#25282F", "#33363F", "#1F2128", "#ECEDEF", "#A1A6B0", "#6C717C",
            "#3DD68C", "#F5B83D", "#F2555A");

        private static readonly Base Light = new(
            "#ECECEF", "#FFFFFF", "#F4F4F6", "#EAEAED", "#DFDFE3", "#FFFFFF",
            "#DCDCE1", "#C6C6CD", "#E6E6EA", "#1D1D1F", "#55555C", "#8A8A90",
            "#1E9E55", "#B7791F", "#D93A3F");

        /// <summary>True when the active app palette is light.</summary>
        public static bool IsLight { get; private set; }

        /// <summary>Raised after a palette was applied; custom-drawn controls repaint.</summary>
        public static event Action? Applied;

        /// <summary>The window background a template gives the app (gallery card backdrops).</summary>
        public static Color WindowColorOf(WheelTemplate template)
        {
            var a = template.App ?? new AppColors();
            var b = IsLightBase(a) ? Light : Dark;
            return PaletteMath.Parse(a.Window, PaletteMath.Parse(b.Window, Colors.Black));
        }

        /// <summary>The template's Convert, Tools and Actions accents (the wheel colors).</summary>
        public static (Color Convert, Color Tools, Color Actions) ModeColorsOf(WheelTemplate template)
        {
            var w = template.Wheel ?? new WheelColors();
            var d = new WheelColors();
            return (PaletteMath.Parse(w.Convert, PaletteMath.Parse(d.Convert, Colors.Orange)),
                    PaletteMath.Parse(w.Tools, PaletteMath.Parse(d.Tools, Colors.DodgerBlue)),
                    PaletteMath.Parse(w.Actions, PaletteMath.Parse(d.Actions, Colors.MediumPurple)));
        }

        /// <summary>The accent the app uses for a template: its own, else the wheel's Convert color.</summary>
        public static Color AccentOf(WheelTemplate template) =>
            PaletteMath.Parse(template.App?.Accent, ModeColorsOf(template).Convert);

        private static bool IsLightBase(AppColors a) => string.Equals(a.Base, "light", StringComparison.OrdinalIgnoreCase);

        public static void Apply(WheelTemplate template)
        {
            var app = Application.Current;
            if (app == null) return;
            var res = app.Resources;

            var a = template.App ?? new AppColors();
            bool light = IsLightBase(a);
            var b = light ? Light : Dark;
            var (convert, tools, actions) = ModeColorsOf(template);
            IsLight = light;

            Color C(string? value, string fallback) => PaletteMath.Parse(value, PaletteMath.Parse(fallback, Colors.Magenta));

            Color window = C(a.Window, b.Window);
            Color surface = C(a.Surface, b.Surface);
            Color raised = C(a.SurfaceRaised, b.SurfaceRaised);
            Color hover = C(a.SurfaceHover, b.SurfaceHover);
            Color pressed = C(a.SurfacePressed, b.SurfacePressed);
            Color border = C(a.Border, b.Border);
            Color borderStrong = C(a.BorderStrong, b.BorderStrong);
            Color accent = PaletteMath.Parse(a.Accent, convert);
            Color success = C(a.Success, b.Success);
            Color warning = C(a.Warning, b.Warning);
            Color danger = C(a.Danger, b.Danger);
            Color white = Colors.White, black = Colors.Black;

            void Brush(string key, Color c) => res[key] = new ImmutableSolidColorBrush(c);

            // Fluent draws the controls this app doesn't restyle (scroll bars, text selection) in the matching variant
            app.RequestedThemeVariant = light ? ThemeVariant.Light : ThemeVariant.Dark;

            Brush("WindowBrush", window);
            Brush("SurfaceBrush", surface);
            Brush("SurfaceRaisedBrush", raised);
            Brush("SurfaceHoverBrush", hover);
            Brush("SurfacePressedBrush", pressed);
            Brush("InputBrush", C(a.Input, b.Input));
            Brush("BorderBrush", border);
            Brush("BorderStrongBrush", borderStrong);
            Brush("DividerBrush", C(a.Divider, b.Divider));
            Brush("TextPrimaryBrush", C(a.TextPrimary, b.TextPrimary));
            Brush("TextSecondaryBrush", C(a.TextSecondary, b.TextSecondary));
            Brush("TextTertiaryBrush", C(a.TextTertiary, b.TextTertiary));

            res["AccentColor"] = accent;
            Brush("AccentBrush", accent);
            Brush("AccentHoverBrush", light ? PaletteMath.Mix(accent, black, 0.08) : PaletteMath.Mix(accent, white, 0.12));
            Brush("AccentPressedBrush", PaletteMath.Mix(accent, black, 0.12));
            Brush("AccentSubtleBrush", PaletteMath.WithAlpha(accent, 0x1F));
            Brush("AccentFaintBrush", PaletteMath.WithAlpha(accent, 0x14));
            Brush("OnAccentBrush", PaletteMath.Luminance(accent) > 0.62 ? Color.FromRgb(0x14, 0x14, 0x18) : white);

            Brush("SuccessBrush", success);
            Brush("SuccessSubtleBrush", PaletteMath.WithAlpha(success, 0x1F));
            Brush("WarningBrush", warning);
            Brush("WarningSubtleBrush", PaletteMath.WithAlpha(warning, 0x1F));
            Brush("DangerBrush", danger);
            Brush("DangerSubtleBrush", PaletteMath.WithAlpha(danger, 0x1F));

            Brush("ModeConvertBrush", convert);
            Brush("ModeToolsBrush", tools);
            Brush("ModeActionsBrush", actions);
            Brush("ModeConvertSubtleBrush", PaletteMath.WithAlpha(convert, 0x1F));
            Brush("ModeToolsSubtleBrush", PaletteMath.WithAlpha(tools, 0x1F));
            Brush("ModeActionsSubtleBrush", PaletteMath.WithAlpha(actions, 0x1F));

            // Tints that sit on top of any surface
            Brush("HoverTintBrush", light ? Color.FromArgb(0x0F, 0, 0, 0) : Color.FromArgb(0x1A, 0xFF, 0xFF, 0xFF));
            Brush("PressTintBrush", light ? Color.FromArgb(0x18, 0, 0, 0) : Color.FromArgb(0x10, 0xFF, 0xFF, 0xFF));
            Brush("DropZoneBrush", light ? Color.FromArgb(0x06, 0, 0, 0) : Color.FromArgb(0x08, 0xFF, 0xFF, 0xFF));
            Brush("ScrollThumbBrush", light ? PaletteMath.Mix(borderStrong, black, 0.06) : PaletteMath.Mix(borderStrong, white, 0.06));
            Brush("ScrollThumbHoverBrush", light ? PaletteMath.Mix(borderStrong, black, 0.2) : PaletteMath.Mix(borderStrong, white, 0.16));
            Brush("PopupBrush", light ? surface : PaletteMath.Lerp(surface, raised, 0.5));

            // The "OG" logo tile: a diagonal gradient around the accent
            res["BrandBrush"] = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                GradientStops =
                {
                    new GradientStop(PaletteMath.Mix(accent, white, 0.18), 0),
                    new GradientStop(PaletteMath.Mix(accent, black, 0.1), 1)
                }
            }.ToImmutable();

            res["CardRadius"] = new CornerRadius(Math.Clamp(a.CardRadius ?? 10, 0, 24));
            res["ControlRadius"] = new CornerRadius(Math.Clamp(a.ControlRadius ?? 6, 0, 16));

            Applied?.Invoke();
        }

        /// <summary>A brush from the app resources (the current palette), or a fallback when it isn't there.</summary>
        public static IBrush Brush(string key)
        {
            if (Application.Current is { } app && app.TryGetResource(key, app.ActualThemeVariant, out object? value) && value is IBrush brush) return brush;
            return Brushes.Magenta;
        }
    }

    /// <summary>Color arithmetic for the palette (same formulas as the Windows ColorUtil).</summary>
    public static class PaletteMath
    {
        public static Color Parse(string? value, Color fallback) =>
            !string.IsNullOrWhiteSpace(value) && Color.TryParse(value.Trim(), out var c) ? c : fallback;

        public static Color WithAlpha(Color c, byte alpha) => Color.FromArgb(alpha, c.R, c.G, c.B);

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
    }
}
