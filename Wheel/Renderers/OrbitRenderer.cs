using System;
using System.Windows;
using System.Windows.Media;

namespace OgConverter.Wheel.Renderers
{
    /// <summary>
    /// Dock-style wheel: options are bubbles on an orbit around the hub. The hovered bubble magnifies and nudges
    /// outward, and its neighbours grow a little, like the macOS Dock.
    /// </summary>
    public sealed class OrbitRenderer : IWheelRenderer
    {
        public string Id => "orbit";
        public string DisplayName => "Orbit";

        private static double BubbleRadius(WheelFrame f, double orbit)
        {
            double chord = f.Count > 1 ? 2 * orbit * Math.Sin(Math.PI / f.Count) : orbit;
            return Math.Max(10 * f.Scale, Math.Min(chord * 0.40, (f.OuterRadius - f.InnerRadius) * 0.40));
        }

        public void Render(DrawingContext dc, WheelFrame f)
        {
            var p = f.Palette;
            double s = f.Scale;
            var c = f.Center;
            double R = f.OuterRadius, r = f.InnerRadius;
            int count = f.Count;
            Color accent = f.Accent;
            double orbit = (r + R) / 2.0;

            // Translucent disc + soft shadow; bubbles carry most of the visual weight
            WheelPainter.DrawShadow(dc, c, R, 30 * s, 8 * s, ColorUtil.WithAlpha(p.Shadow, p.Shadow.A / 255.0 * 0.8));
            var disc = new RadialGradientBrush();
            disc.GradientStops.Add(new GradientStop(ColorUtil.WithAlpha(p.Ring, p.Ring.A / 255.0 * 0.92), 0.55));
            disc.GradientStops.Add(new GradientStop(ColorUtil.WithAlpha(p.Ring, p.Ring.A / 255.0 * 0.78), 1));
            disc.Freeze();
            dc.DrawEllipse(disc, p.EdgePen, c, R, R);

            // Orbit path, with a glowing arc that follows the hovered bubble
            var dash = new Pen(WheelPainter.Solid(p.Divider), 1.2 * s) { DashStyle = new DashStyle(new[] { 1.0, 4.0 }, 0), DashCap = PenLineCap.Round };
            dash.Freeze();
            dc.DrawEllipse(null, dash, c, orbit, orbit);

            double br = BubbleRadius(f, orbit);

            // Draw back to front so the magnified bubble sits on top
            int top = -1;
            double topH = 0.01;
            for (int i = 0; i < count; i++)
            {
                if (f.HoverOf(i) > topH) { topH = f.HoverOf(i); top = i; }
            }

            for (int pass = 0; pass < 2; pass++)
            {
                for (int i = 0; i < count; i++)
                {
                    if ((pass == 0) == (i == top)) continue;
                    DrawBubble(dc, f, i, orbit, br, accent);
                }
            }

            // Hub
            double hubR = r - 10 * s;
            var hub = new RadialGradientBrush { GradientOrigin = new Point(0.5, 0.2), Center = new Point(0.5, 0.4), RadiusX = 0.75, RadiusY = 0.75 };
            hub.GradientStops.Add(new GradientStop(ColorUtil.Mix(p.Hub, Colors.White, p.IsLight ? 0.5 : 0.08), 0));
            hub.GradientStops.Add(new GradientStop(p.Hub, 1));
            hub.Freeze();
            dc.DrawEllipse(hub, WheelPainter.Pen(ColorUtil.WithAlpha(accent, (byte)0x70), 1.5), c, hubR, hubR);

            WheelPainter.DrawHubContent(dc, f, hubR, new WheelPainter.HubStyle
            {
                Title = p.TextBrush,
                Body = p.TextMutedBrush,
                Faint = p.TextFaintBrush,
                Accent = accent
            });
        }

        private static void DrawBubble(DrawingContext dc, WheelFrame f, int i, double orbit, double br, Color accent)
        {
            var p = f.Palette;
            double s = f.Scale;
            int count = f.Count;
            double h = f.HoverOf(i);
            double neighbour = count > 2 ? Math.Max(f.HoverOf((i + 1) % count), f.HoverOf((i - 1 + count) % count)) : 0;
            double mag = 1 + 0.26 * h + 0.08 * neighbour;
            double radius = br * mag;
            var at = f.Polar(orbit + 7 * s * h, f.SliceMid(i));

            using (WheelPainter.Item(dc, f, i, at))
            {
                if (h > 0.01)
                {
                    // Accent glow under the hovered bubble
                    var glow = new RadialGradientBrush();
                    glow.GradientStops.Add(new GradientStop(ColorUtil.WithAlpha(accent, 0.45 * h), 0.55));
                    glow.GradientStops.Add(new GradientStop(ColorUtil.WithAlpha(accent, (byte)0), 1));
                    glow.Freeze();
                    dc.DrawEllipse(glow, null, at, radius * 1.45, radius * 1.45);
                }

                Color idle = ColorUtil.Mix(p.Hub, Colors.White, p.IsLight ? 0.35 : 0.05);
                Color top = ColorUtil.Lerp(ColorUtil.Mix(idle, Colors.White, p.IsLight ? 0.5 : 0.08), ColorUtil.Mix(accent, Colors.White, 0.18), h);
                Color bottom = ColorUtil.Lerp(idle, ColorUtil.Mix(accent, Colors.Black, 0.12), h);
                var fill = new LinearGradientBrush(top, bottom, 90);
                fill.Freeze();
                var rim = WheelPainter.Pen(ColorUtil.Lerp(p.Edge, ColorUtil.WithAlpha(Colors.White, (byte)0x60), h), 1);
                dc.DrawEllipse(fill, rim, at, radius, radius);

                Color onAccent = p.OnAccentFor(accent);
                var glyph = WheelPainter.Solid(ColorUtil.Lerp(p.TextMuted, onAccent, h));
                var label = WheelPainter.Solid(ColorUtil.Lerp(p.Text, onAccent, h));
                WheelPainter.DrawItemContent(dc, f, i, at, radius * 1.62, glyph, label, sizeBoost: Math.Min(mag, 1.12) * (br < 30 * s ? 0.9 : 1.0));
            }
        }
    }
}
