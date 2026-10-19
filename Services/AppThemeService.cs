using System.Windows;
using System.Windows.Media;
using OgConverter.Models;
using OgConverter.Wheel;

namespace OgConverter.Services
{
    /// <summary>
    /// Pushes a template's app palette into Application resources. Every themed color in the XAML is a
    /// DynamicResource, so replacing these entries restyles open windows immediately.
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

        /// <summary>The window background a template gives the app (gallery card backdrops).</summary>
        public static Color WindowColorOf(WheelTemplate template)
        {
            var a = template.App ?? new AppColors();
            var b = string.Equals(a.Base, "light", System.StringComparison.OrdinalIgnoreCase) ? Light : Dark;
            return ColorUtil.Parse(a.Window, ColorUtil.Parse(b.Window, Colors.Black));
        }

        public static void Apply(WheelTemplate template)
        {
            var res = Application.Current?.Resources;
            if (res == null) return;

            var a = template.App ?? new AppColors();
            bool light = string.Equals(a.Base, "light", System.StringComparison.OrdinalIgnoreCase);
            var b = light ? Light : Dark;
            var wheel = WheelPalette.For(template);
            IsLight = light;

            Color C(string? value, string fallback) => ColorUtil.Parse(value, ColorUtil.Parse(fallback, Colors.Magenta));

            Color window = C(a.Window, b.Window);
            Color surface = C(a.Surface, b.Surface);
            Color raised = C(a.SurfaceRaised, b.SurfaceRaised);
            Color hover = C(a.SurfaceHover, b.SurfaceHover);
            Color pressed = C(a.SurfacePressed, b.SurfacePressed);
            Color border = C(a.Border, b.Border);
            Color borderStrong = C(a.BorderStrong, b.BorderStrong);
            Color accent = ColorUtil.Parse(a.Accent, wheel.Convert);
            Color success = C(a.Success, b.Success);
            Color warning = C(a.Warning, b.Warning);
            Color danger = C(a.Danger, b.Danger);
            Color white = Colors.White, black = Colors.Black;

            void Brush(string key, Color c)
            {
                var brush = new SolidColorBrush(c);
                brush.Freeze();
                res[key] = brush;
            }

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
            Brush("AccentHoverBrush", light ? ColorUtil.Mix(accent, black, 0.08) : ColorUtil.Mix(accent, white, 0.12));
            Brush("AccentPressedBrush", ColorUtil.Mix(accent, black, 0.12));
            Brush("AccentSubtleBrush", ColorUtil.WithAlpha(accent, (byte)0x1F));
            Brush("AccentFaintBrush", ColorUtil.WithAlpha(accent, (byte)0x14));
            Brush("OnAccentBrush", ColorUtil.Luminance(accent) > 0.62 ? Color.FromRgb(0x14, 0x14, 0x18) : white);

            Brush("SuccessBrush", success);
            Brush("SuccessSubtleBrush", ColorUtil.WithAlpha(success, (byte)0x1F));
            Brush("WarningBrush", warning);
            Brush("WarningSubtleBrush", ColorUtil.WithAlpha(warning, (byte)0x1F));
            Brush("DangerBrush", danger);
            Brush("DangerSubtleBrush", ColorUtil.WithAlpha(danger, (byte)0x1F));

            Brush("ModeConvertBrush", wheel.Convert);
            Brush("ModeToolsBrush", wheel.Tools);
            Brush("ModeActionsBrush", wheel.Actions);
            Brush("ModeConvertSubtleBrush", ColorUtil.WithAlpha(wheel.Convert, (byte)0x1F));
            Brush("ModeToolsSubtleBrush", ColorUtil.WithAlpha(wheel.Tools, (byte)0x1F));
            Brush("ModeActionsSubtleBrush", ColorUtil.WithAlpha(wheel.Actions, (byte)0x1F));

            // Tints that sit on top of any surface
            Brush("HoverTintBrush", light ? Color.FromArgb(0x0F, 0, 0, 0) : Color.FromArgb(0x1A, 0xFF, 0xFF, 0xFF));
            Brush("PressTintBrush", light ? Color.FromArgb(0x18, 0, 0, 0) : Color.FromArgb(0x10, 0xFF, 0xFF, 0xFF));
            Brush("DropZoneBrush", light ? Color.FromArgb(0x06, 0, 0, 0) : Color.FromArgb(0x08, 0xFF, 0xFF, 0xFF));
            Brush("ScrollThumbBrush", light ? ColorUtil.Mix(borderStrong, black, 0.06) : ColorUtil.Mix(borderStrong, white, 0.06));
            Brush("ScrollThumbHoverBrush", light ? ColorUtil.Mix(borderStrong, black, 0.2) : ColorUtil.Mix(borderStrong, white, 0.16));
            Brush("PopupBrush", light ? surface : ColorUtil.Lerp(surface, raised, 0.5));

            res["BrandStartColor"] = ColorUtil.Mix(accent, white, 0.18);
            res["BrandEndColor"] = ColorUtil.Mix(accent, black, 0.1);

            res["CardRadius"] = new CornerRadius(System.Math.Clamp(a.CardRadius ?? 10, 0, 24));
            res["ControlRadius"] = new CornerRadius(System.Math.Clamp(a.ControlRadius ?? 6, 0, 16));
        }
    }
}
