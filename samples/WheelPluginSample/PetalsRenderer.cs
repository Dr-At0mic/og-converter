using System.Windows;
using System.Windows.Media;
using OgConverter.Wheel;

namespace WheelPluginSample
{
    /// <summary>
    /// Options drawn as petals around the hub. The hovered petal opens wider and fills with the accent.
    /// Any public class implementing IWheelRenderer with a parameterless constructor is picked up.
    /// </summary>
    public sealed class PetalsRenderer : IWheelRenderer
    {
        public string Id => "petals";
        public string DisplayName => "Petals";

        public void Render(DrawingContext dc, WheelFrame f)
        {
            var p = f.Palette;
            double s = f.Scale;
            double R = f.OuterRadius, r = f.InnerRadius;
            double mid = (R + r) / 2.0;

            WheelPainter.DrawShadow(dc, f.Center, R, 28 * s, 6 * s, p.Shadow);

            // Template options are free-form strings, so plugins can be configured from template.json
            double width = f.Template.Options != null && f.Template.Options.TryGetValue("petalWidth", out var w) && double.TryParse(w, out var pw) ? pw : 0.48;

            for (int i = 0; i < f.Count; i++)
            {
                double h = f.HoverOf(i);
                Point at = f.Polar(mid + 4 * s * h, f.SliceMid(i));
                double chord = 2 * mid * System.Math.Sin(System.Math.PI / System.Math.Max(2, f.Count));
                double radial = (R - r) / 2.0 + 4 * s * h;
                double across = chord * (width + 0.08 * h);

                using (WheelPainter.Item(dc, f, i, at))
                {
                    dc.PushTransform(new RotateTransform(f.SliceMid(i) + 90, at.X, at.Y));
                    Color fill = ColorUtil.Lerp(p.Ring, f.Accent, 0.15 + 0.85 * h);
                    dc.DrawEllipse(WheelPainter.Solid(fill), WheelPainter.Pen(ColorUtil.Lerp(p.Edge, f.Accent, h), 1), at, across, radial);
                    dc.Pop();

                    Color text = ColorUtil.Lerp(p.Text, p.OnAccentFor(f.Accent), h);
                    WheelPainter.DrawItemContent(dc, f, i, at, across * 1.6, WheelPainter.Solid(text), WheelPainter.Solid(text));
                }
            }

            double hubR = r - 6 * s;
            dc.DrawEllipse(p.HubBrush, WheelPainter.Pen(ColorUtil.WithAlpha(f.Accent, (byte)0x90), 1.5), f.Center, hubR, hubR);
            WheelPainter.DrawHubContent(dc, f, hubR, new WheelPainter.HubStyle
            {
                Title = p.TextBrush,
                Body = p.TextMutedBrush,
                Faint = p.TextFaintBrush,
                Accent = f.Accent
            });
        }
    }
}
