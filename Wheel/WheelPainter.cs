using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace OgConverter.Wheel
{
    /// <summary>Drawing helpers shared by the built-in renderers and available to plugins.</summary>
    public static class WheelPainter
    {
        public static readonly FontFamily IconFamily = new("Segoe Fluent Icons, Segoe MDL2 Assets, Segoe UI Symbol");
        public static readonly Typeface IconFace = new(IconFamily, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        public const string UiFont = "Segoe UI Variable Text, Segoe UI";
        public const string DisplayFont = "Segoe UI Variable Display, Segoe UI";

        private static readonly Dictionary<(string, int), Typeface> Faces = new();
        private static readonly Dictionary<uint, Brush> SolidCache = new();

        /// <summary>Cached typeface for a font family list and weight.</summary>
        public static Typeface Face(string? family, FontWeight weight)
        {
            family = string.IsNullOrWhiteSpace(family) ? UiFont : family;
            var key = (family, weight.ToOpenTypeWeight());
            if (!Faces.TryGetValue(key, out var face))
            {
                face = new Typeface(new FontFamily(family), FontStyles.Normal, weight, FontStretches.Normal);
                Faces[key] = face;
            }
            return face;
        }

        /// <summary>Frozen, cached solid brush. Safe to call every frame.</summary>
        public static Brush Solid(Color c)
        {
            uint key = (uint)(c.A << 24 | c.R << 16 | c.G << 8 | c.B);
            if (SolidCache.TryGetValue(key, out var b)) return b;
            if (SolidCache.Count > 2048) SolidCache.Clear(); // animated colors create many; keep it bounded
            b = new SolidColorBrush(c);
            b.Freeze();
            SolidCache[key] = b;
            return b;
        }

        public static Pen Pen(Color c, double thickness, bool round = false)
        {
            var p = new Pen(Solid(c), thickness);
            if (round)
            {
                p.StartLineCap = PenLineCap.Round;
                p.EndLineCap = PenLineCap.Round;
                p.LineJoin = PenLineJoin.Round;
            }
            p.Freeze();
            return p;
        }

        public static T Frozen<T>(T f) where T : Freezable
        {
            if (f.CanFreeze) f.Freeze();
            return f;
        }

        // ------------------------------------------------------------------ text

        public static FormattedText Text(string text, Typeface face, double size, Brush brush, double pixelsPerDip) =>
            new(text ?? string.Empty, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, face, Math.Max(1, size), brush, pixelsPerDip);

        /// <summary>Wrapped, centered text trimmed with an ellipsis after <paramref name="maxLines"/>.</summary>
        public static FormattedText Centered(string text, Typeface face, double size, Brush brush, double maxWidth, int maxLines, double pixelsPerDip) =>
            new(text ?? string.Empty, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, face, Math.Max(1, size), brush, pixelsPerDip)
            {
                MaxTextWidth = Math.Max(1, maxWidth),
                MaxLineCount = Math.Max(1, maxLines),
                Trimming = TextTrimming.CharacterEllipsis,
                TextAlignment = TextAlignment.Center
            };

        /// <summary>Draws lines stacked and vertically centered on (cx, cy). Width 0 means the text's natural width.</summary>
        public static void DrawStack(DrawingContext dc, double cx, double cy, IReadOnlyList<(FormattedText Text, double Width, double GapBefore)> lines)
        {
            double total = 0;
            foreach (var l in lines) total += l.GapBefore + l.Text.Height;
            double y = cy - total / 2.0;
            foreach (var l in lines)
            {
                y += l.GapBefore;
                double w = l.Width > 0 ? l.Width : l.Text.Width;
                dc.DrawText(l.Text, new Point(cx - w / 2.0, y));
                y += l.Text.Height;
            }
        }

        // ------------------------------------------------------------------ geometry

        public static Geometry Arc(Point center, double r, double startDeg, double sweepDeg)
        {
            double a0 = startDeg * Math.PI / 180.0, a1 = (startDeg + sweepDeg) * Math.PI / 180.0;
            var g = new StreamGeometry();
            using (var ctx = g.Open())
            {
                ctx.BeginFigure(new Point(center.X + r * Math.Cos(a0), center.Y + r * Math.Sin(a0)), false, false);
                ctx.ArcTo(new Point(center.X + r * Math.Cos(a1), center.Y + r * Math.Sin(a1)), new Size(r, r), 0, sweepDeg > 180, SweepDirection.Clockwise, true, false);
            }
            g.Freeze();
            return g;
        }

        /// <summary>Full ring between two radii.</summary>
        public static Geometry Annulus(Point center, double rInner, double rOuter)
        {
            var g = new StreamGeometry { FillRule = FillRule.EvenOdd };
            using (var ctx = g.Open())
            {
                foreach (double r in new[] { rOuter, rInner })
                {
                    ctx.BeginFigure(new Point(center.X, center.Y - r), true, true);
                    ctx.ArcTo(new Point(center.X, center.Y + r), new Size(r, r), 0, false, SweepDirection.Clockwise, true, false);
                    ctx.ArcTo(new Point(center.X, center.Y - r), new Size(r, r), 0, false, SweepDirection.Clockwise, true, false);
                }
            }
            g.Freeze();
            return g;
        }

        /// <summary>Slice of a ring. A sweep of ~360 returns a full ring.</summary>
        public static Geometry AnnularSector(Point center, double rInner, double rOuter, double startDeg, double sweepDeg) =>
            InsetSector(center, rInner, rOuter, startDeg, sweepDeg, 0, 0);

        /// <summary>
        /// Slice of a ring whose straight sides are moved inward by <paramref name="sideInset"/> (parallel, so the gap
        /// between neighbours is even along its length) and whose arcs are moved inward by <paramref name="radialInset"/>.
        /// </summary>
        public static Geometry InsetSector(Point center, double rInner, double rOuter, double startDeg, double sweepDeg, double sideInset, double radialInset)
        {
            if (sweepDeg >= 359.5) return Annulus(center, rInner + radialInset, rOuter - radialInset);

            double ri = Math.Max(1, rInner + radialInset);
            double ro = Math.Max(ri + 1, rOuter - radialInset);

            // Keep the sides from crossing on narrow slices
            double maxInset = ri * Math.Sin(sweepDeg * Math.PI / 360.0) * 0.95;
            double d = Math.Clamp(sideInset, 0, Math.Max(0, maxInset));

            double a0 = startDeg * Math.PI / 180.0;
            double a1 = (startDeg + sweepDeg) * Math.PI / 180.0;

            // A side offset by d meets a circle of radius R at angle ± asin(d / R) from the side's direction
            double di = Math.Asin(Math.Min(1, d / ri));
            double dO = Math.Asin(Math.Min(1, d / ro));

            Point P(double r, double a) => new(center.X + r * Math.Cos(a), center.Y + r * Math.Sin(a));

            var g = new StreamGeometry();
            using (var ctx = g.Open())
            {
                double outerSweep = (a1 - dO) - (a0 + dO);
                double innerSweep = (a1 - di) - (a0 + di);
                ctx.BeginFigure(P(ri, a0 + di), true, true);
                ctx.LineTo(P(ro, a0 + dO), true, true);
                ctx.ArcTo(P(ro, a1 - dO), new Size(ro, ro), 0, outerSweep > Math.PI, SweepDirection.Clockwise, true, true);
                ctx.LineTo(P(ri, a1 - di), true, true);
                ctx.ArcTo(P(ri, a0 + di), new Size(ri, ri), 0, innerSweep > Math.PI, SweepDirection.Counterclockwise, true, true);
            }
            g.Freeze();
            return g;
        }

        /// <summary>
        /// Fills a ring slice with rounded corners and an even <paramref name="gap"/> to its neighbours. The shape is
        /// inset by the corner radius and stroked with a round-joined pen of twice that width, which grows it back to
        /// full size with exactly rounded corners.
        /// </summary>
        public static void FillRoundedSector(DrawingContext dc, Brush fill, Point center, double rInner, double rOuter,
                                             double startDeg, double sweepDeg, double gap, double corner) =>
            dc.DrawGeometry(fill, null, RoundedSector(center, rInner, rOuter, startDeg, sweepDeg, gap, corner));

        private static readonly Dictionary<(long, long, long, long, long, long, long, long), Geometry> RoundedCache = new();

        /// <summary>
        /// Ring slice with rounded corners and an even <paramref name="gap"/> to its neighbours: the slice is inset by
        /// the corner radius, then grown back by a round-joined outline of that radius, which rounds every corner
        /// exactly. The result is one outline, so translucent fills don't double up where the parts overlap.
        /// Cached, since a wheel redraws the same few shapes on every animation frame.
        /// </summary>
        public static Geometry RoundedSector(Point center, double rInner, double rOuter, double startDeg, double sweepDeg, double gap, double corner)
        {
            corner = Math.Max(0, Math.Min(corner, (rOuter - rInner) / 2.0 - 0.5));
            var geom = InsetSector(center, rInner, rOuter, startDeg, sweepDeg, gap / 2.0 + corner, corner);
            if (corner < 0.5) return geom;

            static long Q(double v) => (long)Math.Round(v * 4);
            var key = (Q(center.X), Q(center.Y), Q(rInner), Q(rOuter), Q(startDeg), Q(sweepDeg), Q(gap), Q(corner));
            if (RoundedCache.TryGetValue(key, out var cached)) return cached;

            var pen = new Pen(Brushes.Black, corner * 2) { LineJoin = PenLineJoin.Round };
            var outline = Geometry.Combine(geom, geom.GetWidenedPathGeometry(pen, 0.2, ToleranceType.Absolute), GeometryCombineMode.Union, null, 0.2, ToleranceType.Absolute);
            outline.Freeze();
            if (RoundedCache.Count > 512) RoundedCache.Clear();
            RoundedCache[key] = outline;
            return outline;
        }

        // ------------------------------------------------------------------ item content

        private sealed class Popper : IDisposable
        {
            private readonly DrawingContext _dc;
            private readonly int _count;
            public Popper(DrawingContext dc, int count) { _dc = dc; _count = count; }
            public void Dispose() { for (int i = 0; i < _count; i++) _dc.Pop(); }
        }

        /// <summary>
        /// Applies an option's entrance animation (opacity and scale around <paramref name="itemCenter"/>).
        /// Use as <c>using (WheelPainter.Item(dc, f, i, center)) { ... }</c>.
        /// </summary>
        public static IDisposable Item(DrawingContext dc, WheelFrame f, int index, Point itemCenter)
        {
            int pushes = 0;
            double alpha = f.ItemAlpha(index);
            double scale = f.ItemScale(index);
            if (alpha < 0.999) { dc.PushOpacity(Math.Max(0, alpha)); pushes++; }
            if (Math.Abs(scale - 1) > 0.001) { dc.PushTransform(new ScaleTransform(scale, scale, itemCenter.X, itemCenter.Y)); pushes++; }
            return new Popper(dc, pushes);
        }

        /// <summary>
        /// Draws an option's icon and label centered on <paramref name="at"/>, sized for the frame's density.
        /// Micro wheels show short titles (file formats) as text and everything else as an icon.
        /// </summary>
        public static void DrawItemContent(DrawingContext dc, WheelFrame f, int index, Point at, double maxWidth,
                                           Brush glyphBrush, Brush labelBrush, Typeface? labelFace = null, double sizeBoost = 1.0)
        {
            var item = f.Items[index];
            string label = f.LabelOf(index);
            labelFace ??= Face(f.Template.Shape.Font, FontWeights.SemiBold);
            double ts = f.TextScale * sizeBoost;
            double ppd = f.PixelsPerDip;

            if (f.Density == WheelDensity.Micro)
            {
                FormattedText t = label.Length <= 5 || string.IsNullOrEmpty(item.IconGlyph)
                    ? Centered(label, Face(f.Template.Shape.Font, FontWeights.Bold), 9.5 * ts, labelBrush, maxWidth, 1, ppd)
                    : Text(item.IconGlyph, IconFace, 14 * ts, glyphBrush, ppd);
                double w = t.MaxTextWidth > 1 && t.TextAlignment == TextAlignment.Center ? t.MaxTextWidth : t.Width;
                dc.DrawText(t, new Point(at.X - w / 2.0, at.Y - t.Height / 2.0));
                return;
            }

            bool compact = f.Density == WheelDensity.Compact;
            var glyph = Text(item.IconGlyph, IconFace, (compact ? 17 : 19) * ts, glyphBrush, ppd);
            var text = Centered(label, labelFace, (compact ? 10.5 : 11.5) * ts, labelBrush, maxWidth, 2, ppd);

            double gap = 6 * f.Scale;
            double blockH = glyph.Height + gap + text.Height;
            double y = at.Y - blockH / 2.0;
            dc.DrawText(glyph, new Point(at.X - glyph.Width / 2.0, y));
            dc.DrawText(text, new Point(at.X - maxWidth / 2.0, y + glyph.Height + gap));
        }

        /// <summary>Label width that fits a slice at radius <paramref name="r"/>.</summary>
        public static double SliceLabelWidth(WheelFrame f, double r)
        {
            double chord = 2 * r * Math.Sin(Math.Min(f.SliceAngle, 180) * Math.PI / 360.0);
            return Math.Max(34 * f.Scale, Math.Min(chord * 0.86, (f.OuterRadius - f.InnerRadius) * 1.25));
        }

        // ------------------------------------------------------------------ hub

        /// <summary>Colors and fonts for <see cref="DrawHubContent"/>.</summary>
        public sealed class HubStyle
        {
            public Brush Title { get; init; } = Brushes.White;
            public Brush Body { get; init; } = Brushes.Gray;
            public Brush Faint { get; init; } = Brushes.Gray;
            public Color Accent { get; init; }
            public Typeface? TitleFace { get; init; }
            public Typeface? BodyFace { get; init; }
            public Typeface? BadgeFace { get; init; }
            public bool UppercaseTitle { get; init; }
        }

        /// <summary>
        /// Hub text: the hovered option's icon, title and description; or the file's type pill, name and a hint;
        /// or an icon with a message when there is no file.
        /// </summary>
        public static void DrawHubContent(DrawingContext dc, WheelFrame f, double hubRadius, HubStyle style)
        {
            double s = f.Scale;
            double ts = f.TextScale;
            double ppd = f.PixelsPerDip;
            double cx = f.Center.X, cy = f.Center.Y;
            var titleFace = style.TitleFace ?? Face(DisplayFont, FontWeights.SemiBold);
            var bodyFace = style.BodyFace ?? Face(UiFont, FontWeights.Normal);
            var badgeFace = style.BadgeFace ?? Face(UiFont, FontWeights.Bold);
            var accentBrush = Solid(style.Accent);
            bool compact = f.Density != WheelDensity.Full;
            bool micro = f.Density == WheelDensity.Micro;
            int hovered = f.HoveredIndex;

            if (hovered >= 0 && hovered < f.Count)
            {
                var item = f.Items[hovered];
                string title = style.UppercaseTitle ? item.Title.ToUpperInvariant() : item.Title;
                var lines = new List<(FormattedText, double, double)>();
                if (micro)
                {
                    lines.Add((Centered(title, titleFace, 9.5 * ts, style.Title, hubRadius * 1.8, 2, ppd), hubRadius * 1.8, 0));
                }
                else
                {
                    lines.Add((Text(item.IconGlyph, IconFace, (compact ? 18 : 22) * ts, accentBrush, ppd), 0, 0));
                    lines.Add((Centered(title, titleFace, (compact ? 12 : 14) * ts, style.Title, hubRadius * 1.7, compact ? 2 : 1, ppd), hubRadius * 1.7, 7 * s));
                    if (!compact && !string.IsNullOrEmpty(item.Description))
                        lines.Add((Centered(item.Description, bodyFace, 10.5 * ts, style.Body, hubRadius * 1.5, 3, ppd), hubRadius * 1.5, 4 * s));
                }
                DrawStack(dc, cx, cy, lines);
                return;
            }

            if (!string.IsNullOrEmpty(f.CenterBadge))
            {
                var badge = Text(f.CenterBadge, badgeFace, (micro ? 8.5 : 10) * ts, accentBrush, ppd);
                double padX = (micro ? 5 : 7) * s, padY = 2.5 * s;
                double pillW = badge.Width + padX * 2, pillH = badge.Height + padY * 2;

                if (micro)
                {
                    var microPill = new Rect(cx - pillW / 2.0, cy - pillH / 2.0, pillW, pillH);
                    dc.DrawRoundedRectangle(Solid(ColorUtil.WithAlpha(style.Accent, (byte)0x26)), null, microPill, 4 * s, 4 * s);
                    dc.DrawText(badge, new Point(microPill.X + padX, microPill.Y + padY));
                    return;
                }

                var name = Centered(f.CenterSubtitle, titleFace, (compact ? 11.5 : 13) * ts, style.Title, hubRadius * 1.7, compact ? 1 : 2, ppd);
                var hint = Centered(compact ? string.Empty : f.CenterTitle, bodyFace, 11 * ts, style.Faint, hubRadius * 1.6, 1, ppd);
                double total = pillH + 8 * s + name.Height + 4 * s + hint.Height;
                double y = cy - total / 2.0;

                var pill = new Rect(cx - pillW / 2.0, y, pillW, pillH);
                dc.DrawRoundedRectangle(Solid(ColorUtil.WithAlpha(style.Accent, (byte)0x26)), null, pill, 4 * s, 4 * s);
                dc.DrawText(badge, new Point(pill.X + padX, pill.Y + padY));
                y += pillH + 8 * s;
                dc.DrawText(name, new Point(cx - name.MaxTextWidth / 2.0, y));
                y += name.Height + 4 * s;
                dc.DrawText(hint, new Point(cx - hint.MaxTextWidth / 2.0, y));
                return;
            }

            var msg = new List<(FormattedText, double, double)>
            {
                (Text(f.CenterGlyph, IconFace, (micro ? 15 : compact ? 18 : 22) * ts, style.Faint, ppd), 0, 0)
            };
            if (!micro)
            {
                msg.Add((Centered(f.CenterTitle, titleFace, (compact ? 11.5 : 13) * ts, style.Title, hubRadius * 1.7, 2, ppd), hubRadius * 1.7, 7 * s));
                if (!compact && !string.IsNullOrEmpty(f.CenterSubtitle))
                    msg.Add((Centered(f.CenterSubtitle, bodyFace, 11 * ts, style.Faint, hubRadius * 1.6, 2, ppd), hubRadius * 1.6, 3 * s));
            }
            DrawStack(dc, cx, cy, msg);
        }

        /// <summary>Soft round shadow under the wheel.</summary>
        public static void DrawShadow(DrawingContext dc, Point center, double radius, double spread, double offsetY, Color shadow)
        {
            if (shadow.A == 0) return;
            double r = radius + spread;
            var brush = new RadialGradientBrush { Center = new Point(0.5, 0.5), GradientOrigin = new Point(0.5, 0.5), RadiusX = 0.5, RadiusY = 0.5 };
            brush.GradientStops.Add(new GradientStop(shadow, Math.Clamp((radius - spread) / r, 0, 1)));
            brush.GradientStops.Add(new GradientStop(ColorUtil.WithAlpha(shadow, (byte)0), 1.0));
            brush.Freeze();
            dc.DrawEllipse(brush, null, new Point(center.X, center.Y + offsetY), r, r);
        }
    }
}
