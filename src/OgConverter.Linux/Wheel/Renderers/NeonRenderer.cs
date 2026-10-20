using System;
using Avalonia;
using Avalonia.Media;

namespace OgConverter.Wheel.Renderers
{
    /// <summary>
    /// Synthwave HUD: a dark ring outlined in glowing neon, tick marks around the rim and a neon-traced selection.
    /// Glow is layered strokes rather than a blur effect, so it stays cheap to animate.
    /// </summary>
    public sealed class NeonRenderer : IWheelRenderer
    {
        public string Id => "neon";
        public string DisplayName => "Neon";

        private static readonly (double Width, double Alpha)[] GlowLayers = { (16, 0.06), (9, 0.12), (4.5, 0.28), (1.6, 1.0) };

        private static void Glow(DrawingContext dc, Geometry g, Color color, double scale, double strength = 1)
        {
            foreach (var (w, a) in GlowLayers)
            {
                dc.DrawGeometry(null, WheelPainter.Pen(ColorUtil.WithAlpha(color, a * strength), Math.Max(1, w * scale), round: true), g);
            }
        }

        private static void GlowCircle(DrawingContext dc, Point c, double radius, Color color, double scale, double strength = 1)
        {
            foreach (var (w, a) in GlowLayers)
            {
                dc.DrawEllipse(null, WheelPainter.Pen(ColorUtil.WithAlpha(color, a * strength), Math.Max(1, w * scale)), c, radius, radius);
            }
        }

        public void Render(DrawingContext dc, WheelFrame f)
        {
            var p = f.Palette;
            double s = f.Scale;
            var c = f.Center;
            double R = f.OuterRadius, r = f.InnerRadius;
            int count = f.Count;
            double sweep = f.SliceAngle;
            Color accent = f.Accent;

            WheelPainter.DrawShadow(dc, c, R, 24 * s, 0, p.Shadow);

            // Body with a faint accent haze toward the rim
            var body = WheelPainter.Radial((p.Ring, 0.6), (ColorUtil.Mix(p.Ring, accent, 0.10), 1));
            dc.DrawEllipse(body, null, c, R, R);

            GlowCircle(dc, c, R, accent, s, 0.85);

            // HUD ticks: every 5°, longer every 30°, rotating with the open animation
            var tick = WheelPainter.Pen(p.Divider, Math.Max(1, 1 * s));
            var tickMajor = WheelPainter.Pen(ColorUtil.WithAlpha(accent, (byte)0x90), Math.Max(1, 1.4 * s));
            for (int a = 0; a < 360; a += 5)
            {
                bool major = a % 30 == 0;
                double ang = a + f.Rotation;
                dc.DrawLine(major ? tickMajor : tick, f.Polar(R - (major ? 9 : 5) * s, ang), f.Polar(R - 2 * s, ang));
            }

            if (count > 1)
            {
                var divider = WheelPainter.Pen(ColorUtil.WithAlpha(accent, (byte)0x38), 1);
                for (int i = 0; i < count; i++)
                {
                    double a = f.SliceStart(i);
                    dc.DrawLine(divider, f.Polar(r + 8 * s, a), f.Polar(R - 14 * s, a));
                }
            }

            for (int i = 0; i < count; i++)
            {
                double h = f.HoverOf(i);
                if (h < 0.01) continue;
                double start = f.SliceStart(i);
                var sector = WheelPainter.InsetSector(c, r + 4 * s, R - 12 * s, start, sweep, 3 * s, 0);
                dc.DrawGeometry(WheelPainter.Solid(ColorUtil.WithAlpha(accent, 0.16 * h)), null, sector);
                Glow(dc, sector, accent, s, h * 0.9);
                // Bright arc just outside the rim marks the selection from a distance
                Glow(dc, WheelPainter.Arc(c, R + 7 * s, start + 2, sweep - 4), accent, s, h);
            }

            var mono = WheelPainter.Face(f.Template.Shape.Font ?? "Cascadia Mono, Consolas", FontWeight.SemiBold);
            for (int i = 0; i < count; i++)
            {
                double h = f.HoverOf(i);
                double rMid = (r + R - 8 * s) / 2.0;
                var at = f.Polar(rMid, f.SliceMid(i));
                var glyph = WheelPainter.Solid(ColorUtil.Lerp(ColorUtil.Mix(accent, p.TextMuted, 0.45), Colors.White, h));
                var label = WheelPainter.Solid(ColorUtil.Lerp(p.Text, ColorUtil.Mix(accent, Colors.White, 0.55), h));
                using (WheelPainter.Item(dc, f, i, at))
                {
                    WheelPainter.DrawItemContent(dc, f, i, at, WheelPainter.SliceLabelWidth(f, rMid) * 0.95, glyph, label, mono, 0.95);
                }
            }

            // Hub: dark core, neon rim and a dashed inner ring
            double hubR = r - 4 * s;
            dc.DrawEllipse(p.HubBrush, null, c, hubR, hubR);
            GlowCircle(dc, c, hubR, accent, s * 0.7, 0.8);
            var dashed = WheelPainter.DashedPen(ColorUtil.WithAlpha(accent, (byte)0x55), Math.Max(1, 1 * s), new[] { 2.0, 3.0 }, f.Rotation / 10.0);
            dc.DrawEllipse(null, dashed, c, hubR - 6 * s, hubR - 6 * s);

            WheelPainter.DrawHubContent(dc, f, hubR - 6 * s, new WheelPainter.HubStyle
            {
                Title = p.TextBrush,
                Body = p.TextMutedBrush,
                Faint = p.TextFaintBrush,
                Accent = accent,
                TitleFace = mono,
                BadgeFace = WheelPainter.Face("Cascadia Mono, Consolas", FontWeight.Bold),
                UppercaseTitle = true
            });
        }
    }
}
