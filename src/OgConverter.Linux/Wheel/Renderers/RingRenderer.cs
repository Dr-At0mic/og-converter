using System;
using Avalonia;
using Avalonia.Media;

namespace OgConverter.Wheel.Renderers
{
    /// <summary>The original OG wheel: one seamless ring, hairline dividers, a tinted sector and accent arc on hover.</summary>
    public sealed class RingRenderer : IWheelRenderer
    {
        public string Id => "ring";
        public string DisplayName => "Ring";

        public void Render(DrawingContext dc, WheelFrame f)
        {
            var p = f.Palette;
            double s = f.Scale;
            var center = f.Center;
            double R = f.OuterRadius, r = f.InnerRadius;
            Color accent = f.Accent;
            int count = f.Count;
            double sweep = f.SliceAngle;

            WheelPainter.DrawShadow(dc, center, R, 26 * s, 6 * s, p.Shadow);

            dc.DrawEllipse(p.RingBrush, p.EdgePen, center, R, R);
            var sheen = WheelPainter.Linear(new Point(0.5, 0), new Point(0.5, 0.55), RelativeUnit.Relative,
                (Color.FromArgb(p.IsLight ? (byte)0x40 : (byte)0x0E, 0xFF, 0xFF, 0xFF), 0),
                (Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF), 1));
            dc.DrawEllipse(sheen, null, center, R, R);

            if (count > 1)
            {
                for (int i = 0; i < count; i++)
                {
                    double a = f.SliceStart(i);
                    dc.DrawLine(p.DividerPen, f.Polar(r + 6 * s, a), f.Polar(R - 6 * s, a));
                }
            }

            // Hover highlight, faded by the animated hover amount so moving between options glides
            for (int i = 0; i < count; i++)
            {
                double h = f.HoverOf(i);
                if (h < 0.01) continue;
                double start = f.SliceStart(i);
                double rOut = R + 6 * s * h;
                var sector = WheelPainter.AnnularSector(center, r + 2 * s, rOut, start + 0.6, sweep - 1.2);

                var fill = WheelPainter.RadialAbsolute(center, rOut,
                    (ColorUtil.WithAlpha(accent, 0x1E / 255.0 * h), r / rOut),
                    (ColorUtil.WithAlpha(accent, 0x50 / 255.0 * h), 1.0));
                dc.DrawGeometry(fill, WheelPainter.Pen(ColorUtil.WithAlpha(accent, 0x60 / 255.0 * h), 1), sector);

                double inset = Math.Min(4, sweep * 0.08);
                dc.DrawGeometry(null, WheelPainter.Pen(ColorUtil.WithAlpha(accent, h), 3 * s, round: true),
                    WheelPainter.Arc(center, rOut - 1.5 * s, start + inset, sweep - inset * 2));
            }

            for (int i = 0; i < count; i++)
            {
                double h = f.HoverOf(i);
                double rMid = (r + R) / 2.0 + 3 * s * h;
                var at = f.Polar(rMid, f.SliceMid(i));
                var glyph = WheelPainter.Solid(ColorUtil.Lerp(p.TextMuted, accent, h));
                var label = WheelPainter.Solid(ColorUtil.Lerp(p.Text, p.IsLight ? p.Text : Colors.White, h));
                using (WheelPainter.Item(dc, f, i, at))
                {
                    WheelPainter.DrawItemContent(dc, f, i, at, WheelPainter.SliceLabelWidth(f, rMid), glyph, label);
                }
            }

            double hubR = r - 7 * s;
            dc.DrawEllipse(p.RingBrush, p.EdgePen, center, r, r);
            dc.DrawEllipse(p.HubBrush, WheelPainter.Pen(ColorUtil.WithAlpha(accent, (byte)0x8C), 1.5), center, hubR, hubR);

            WheelPainter.DrawHubContent(dc, f, hubR, new WheelPainter.HubStyle
            {
                Title = p.TextBrush,
                Body = p.TextMutedBrush,
                Faint = p.TextFaintBrush,
                Accent = accent
            });
        }
    }
}
