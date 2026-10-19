using System;
using System.Windows;
using System.Windows.Media;

namespace OgConverter.Wheel.Renderers
{
    /// <summary>
    /// macOS-style wheel: separate rounded tiles with even gaps, a frosted vertical gradient, and the selection
    /// filled with the accent like a menu highlight.
    /// </summary>
    public sealed class GlassRenderer : IWheelRenderer
    {
        public string Id => "glass";
        public string DisplayName => "Glass";

        public void Render(DrawingContext dc, WheelFrame f)
        {
            var p = f.Palette;
            double s = f.Scale;
            var c = f.Center;
            double R = f.OuterRadius, r = f.InnerRadius;
            int count = f.Count;
            double sweep = f.SliceAngle;
            Color accent = f.Accent;
            double gap = Math.Max(2, (f.Template.Shape.Gap > 0 ? f.Template.Shape.Gap : 6) * s);
            double corner = (f.Template.Shape.CornerRadius > 0 ? f.Template.Shape.CornerRadius : 12) * s;

            // Two shadows: a wide ambient one and a tighter key shadow, like a macOS popover
            WheelPainter.DrawShadow(dc, c, R, 34 * s, 10 * s, ColorUtil.WithAlpha(p.Shadow, p.Shadow.A / 255.0 * 0.6));
            WheelPainter.DrawShadow(dc, c, R, 10 * s, 3 * s, p.Shadow);

            // Frosted surface: lighter at the top, base color at the bottom
            var tile = new LinearGradientBrush
            {
                MappingMode = BrushMappingMode.Absolute,
                StartPoint = new Point(c.X, c.Y - R),
                EndPoint = new Point(c.X, c.Y + R)
            };
            tile.GradientStops.Add(new GradientStop(ColorUtil.Mix(p.Ring, Colors.White, p.IsLight ? 0.55 : 0.07), 0));
            tile.GradientStops.Add(new GradientStop(p.Ring, 0.55));
            tile.GradientStops.Add(new GradientStop(ColorUtil.Mix(p.Ring, Colors.Black, p.IsLight ? 0.04 : 0.12), 1));
            tile.Freeze();

            if (count == 0)
            {
                WheelPainter.FillRoundedSector(dc, tile, c, r + gap, R, 0, 360, 0, 0);
            }

            for (int i = 0; i < count; i++)
            {
                double h = f.HoverOf(i);
                double lift = 4 * s * h;
                Brush fill = h < 0.01 ? tile : WheelPainter.Solid(ColorUtil.Lerp(ColorUtil.WithAlpha(accent, (byte)0), accent, h));

                if (count == 1)
                {
                    dc.DrawGeometry(tile, null, WheelPainter.Annulus(c, r + gap, R));
                    if (h >= 0.01) dc.DrawGeometry(fill, null, WheelPainter.Annulus(c, r + gap, R + lift));
                    continue;
                }

                WheelPainter.FillRoundedSector(dc, tile, c, r + gap, R + lift, f.SliceStart(i), sweep, gap, corner);
                if (h >= 0.01)
                {
                    WheelPainter.FillRoundedSector(dc, fill, c, r + gap, R + lift, f.SliceStart(i), sweep, gap, corner);
                }
            }

            // Hairline rim so light tiles separate from light desktops
            dc.DrawEllipse(null, WheelPainter.Pen(ColorUtil.WithAlpha(p.Edge, p.Edge.A / 255.0 * 0.7), 1), c, R + 0.5, R + 0.5);

            Color onAccent = p.OnAccentFor(accent);
            for (int i = 0; i < count; i++)
            {
                double h = f.HoverOf(i);
                double rMid = (r + gap + R) / 2.0 + 2 * s * h;
                var at = f.Polar(rMid, f.SliceMid(i));
                var glyph = WheelPainter.Solid(ColorUtil.Lerp(p.TextMuted, onAccent, h));
                var label = WheelPainter.Solid(ColorUtil.Lerp(p.Text, onAccent, h));
                using (WheelPainter.Item(dc, f, i, at))
                {
                    WheelPainter.DrawItemContent(dc, f, i, at, WheelPainter.SliceLabelWidth(f, rMid) * 0.92, glyph, label);
                }
            }

            // Hub: a separate frosted disc with a soft inner highlight
            double hubR = r - 2 * s;
            var hub = new RadialGradientBrush { GradientOrigin = new Point(0.5, 0.15), Center = new Point(0.5, 0.35), RadiusX = 0.8, RadiusY = 0.8 };
            hub.GradientStops.Add(new GradientStop(ColorUtil.Mix(p.Hub, Colors.White, p.IsLight ? 0.6 : 0.08), 0));
            hub.GradientStops.Add(new GradientStop(p.Hub, 1));
            hub.Freeze();
            dc.DrawEllipse(hub, WheelPainter.Pen(p.Edge, 1), c, hubR, hubR);

            WheelPainter.DrawHubContent(dc, f, hubR - 4 * s, new WheelPainter.HubStyle
            {
                Title = p.TextBrush,
                Body = p.TextMutedBrush,
                Faint = p.TextFaintBrush,
                Accent = accent
            });
        }
    }
}
