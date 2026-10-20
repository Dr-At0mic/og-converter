using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace OgConverter.Wheel
{
    /// <summary>Drawing helpers shared by the built-in renderers. The Avalonia port of the Windows app's WheelPainter.</summary>
    public static class WheelPainter
    {
        public static FontFamily IconFamily => Icons.Icons.Font;
        public static readonly Typeface IconFace = new(Icons.Icons.Font, FontStyle.Normal, FontWeight.Normal, FontStretch.Normal);

        /// <summary>Segoe UI on systems that have it, else the usual Linux desktop fonts.</summary>
        public const string UiFont = "Segoe UI Variable Text, Segoe UI, Inter, Adwaita Sans, Cantarell, Ubuntu Sans, Noto Sans, DejaVu Sans";
        public const string DisplayFont = "Segoe UI Variable Display, Segoe UI, Inter, Adwaita Sans, Cantarell, Ubuntu Sans, Noto Sans, DejaVu Sans";

        /// <summary>Appended to monospaced template fonts (Neon asks for Cascadia Mono / Consolas, which Linux rarely has).</summary>
        private const string MonoFallbacks = "Cascadia Mono, Ubuntu Sans Mono, Ubuntu Mono, DejaVu Sans Mono, Liberation Mono, Noto Mono";

        private static readonly Dictionary<(string, int), Typeface> Faces = new();
        private static readonly Dictionary<uint, ImmutableSolidColorBrush> SolidCache = new();
        private static readonly Dictionary<(uint, int, bool), ImmutablePen> PenCache = new();

        /// <summary>Cached typeface for a font family list and weight. Template fonts get Linux fallbacks.</summary>
        public static Typeface Face(string? family, FontWeight weight)
        {
            family = string.IsNullOrWhiteSpace(family) ? UiFont : family;
            var key = (family, (int)weight);
            if (!Faces.TryGetValue(key, out var face))
            {
                string list = family;
                if (family != UiFont && family != DisplayFont)
                {
                    bool mono = family.Contains("Mono", StringComparison.OrdinalIgnoreCase) || family.Contains("Consolas", StringComparison.OrdinalIgnoreCase)
                                || family.Contains("Courier", StringComparison.OrdinalIgnoreCase);
                    list = family + ", " + (mono ? MonoFallbacks : UiFont);
                }
                face = new Typeface(new FontFamily(list), FontStyle.Normal, weight, FontStretch.Normal);
                Faces[key] = face;
            }
            return face;
        }

        /// <summary>Immutable, cached solid brush. Safe to call every frame.</summary>
        public static IBrush Solid(Color c)
        {
            uint key = c.ToUInt32();
            if (SolidCache.TryGetValue(key, out var b)) return b;
            if (SolidCache.Count > 2048) SolidCache.Clear(); // animated colors create many; keep it bounded
            b = new ImmutableSolidColorBrush(c);
            SolidCache[key] = b;
            return b;
        }

        /// <summary>Immutable, cached pen.</summary>
        public static IPen Pen(Color c, double thickness, bool round = false)
        {
            var key = (c.ToUInt32(), (int)Math.Round(thickness * 100), round);
            if (PenCache.TryGetValue(key, out var p)) return p;
            if (PenCache.Count > 2048) PenCache.Clear();
            p = new ImmutablePen((ImmutableSolidColorBrush)Solid(c), thickness, null,
                round ? PenLineCap.Round : PenLineCap.Flat, round ? PenLineJoin.Round : PenLineJoin.Miter, 10);
            PenCache[key] = p;
            return p;
        }

        /// <summary>A pen with a dash pattern (in multiples of the thickness, like WPF).</summary>
        public static IPen DashedPen(Color c, double thickness, double[] dashes, double offset = 0, bool round = false) =>
            new ImmutablePen((ImmutableSolidColorBrush)Solid(c), thickness, new ImmutableDashStyle(dashes, offset),
                round ? PenLineCap.Round : PenLineCap.Flat, PenLineJoin.Miter, 10);

        // ------------------------------------------------------------------ text

        public static FormattedText Text(string text, Typeface face, double size, IBrush brush) =>
            new(text ?? string.Empty, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, face, Math.Max(1, size), brush);

        /// <summary>An icon: <paramref name="segoeGlyph"/> is a Segoe Fluent Icons code point, mapped to the bundled font.</summary>
        public static FormattedText Icon(string segoeGlyph, double size, IBrush brush) =>
            Text(Icons.Icons.Glyph(segoeGlyph), IconFace, size, brush);

        /// <summary>Wrapped, centered text trimmed with an ellipsis after <paramref name="maxLines"/>.</summary>
        public static FormattedText Centered(string text, Typeface face, double size, IBrush brush, double maxWidth, int maxLines) =>
            new(text ?? string.Empty, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, face, Math.Max(1, size), brush)
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
                ctx.BeginFigure(new Point(center.X + r * Math.Cos(a0), center.Y + r * Math.Sin(a0)), false);
                ctx.ArcTo(new Point(center.X + r * Math.Cos(a1), center.Y + r * Math.Sin(a1)), new Size(r, r), 0, sweepDeg > 180, SweepDirection.Clockwise);
                ctx.EndFigure(false);
            }
            return g;
        }

        /// <summary>Full ring between two radii.</summary>
        public static Geometry Annulus(Point center, double rInner, double rOuter)
        {
            var g = new StreamGeometry();
            using (var ctx = g.Open())
            {
                ctx.SetFillRule(FillRule.EvenOdd);
                foreach (double r in new[] { rOuter, rInner })
                {
                    ctx.BeginFigure(new Point(center.X, center.Y - r), true);
                    ctx.ArcTo(new Point(center.X, center.Y + r), new Size(r, r), 0, false, SweepDirection.Clockwise);
                    ctx.ArcTo(new Point(center.X, center.Y - r), new Size(r, r), 0, false, SweepDirection.Clockwise);
                    ctx.EndFigure(true);
                }
            }
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
                ctx.BeginFigure(P(ri, a0 + di), true);
                ctx.LineTo(P(ro, a0 + dO));
                ctx.ArcTo(P(ro, a1 - dO), new Size(ro, ro), 0, outerSweep > Math.PI, SweepDirection.Clockwise);
                ctx.LineTo(P(ri, a1 - di));
                ctx.ArcTo(P(ri, a0 + di), new Size(ri, ri), 0, innerSweep > Math.PI, SweepDirection.CounterClockwise);
                ctx.EndFigure(true);
            }
            return g;
        }

        /// <summary>Fills a ring slice with rounded corners and an even <paramref name="gap"/> to its neighbours.</summary>
        public static void FillRoundedSector(DrawingContext dc, IBrush fill, Point center, double rInner, double rOuter,
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
            Geometry outline = new CombinedGeometry(GeometryCombineMode.Union, geom, geom.GetWidenedGeometry(pen));
            if (RoundedCache.Count > 512) RoundedCache.Clear();
            RoundedCache[key] = outline;
            return outline;
        }

        // ------------------------------------------------------------------ item content

        private sealed class Popper : IDisposable
        {
            private readonly DrawingContext.PushedState[] _states;
            private readonly int _count;
            public Popper(DrawingContext.PushedState[] states, int count) { _states = states; _count = count; }
            public void Dispose() { for (int i = _count - 1; i >= 0; i--) _states[i].Dispose(); }
        }

        private static readonly IDisposable NoPush = new Popper(Array.Empty<DrawingContext.PushedState>(), 0);

        /// <summary>Scale by <paramref name="scale"/> around <paramref name="origin"/>.</summary>
        public static Matrix ScaleAt(double scale, Point origin) =>
            Matrix.CreateTranslation(-origin.X, -origin.Y) * Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(origin.X, origin.Y);

        /// <summary>
        /// Applies an option's entrance animation (opacity and scale around <paramref name="itemCenter"/>).
        /// Use as <c>using (WheelPainter.Item(dc, f, i, center)) { ... }</c>.
        /// </summary>
        public static IDisposable Item(DrawingContext dc, WheelFrame f, int index, Point itemCenter)
        {
            double alpha = f.ItemAlpha(index);
            double scale = f.ItemScale(index);
            bool fade = alpha < 0.999, grow = Math.Abs(scale - 1) > 0.001;
            if (!fade && !grow) return NoPush;

            var states = new DrawingContext.PushedState[2];
            int n = 0;
            if (fade) states[n++] = dc.PushOpacity(Math.Max(0, alpha));
            if (grow) states[n++] = dc.PushTransform(ScaleAt(scale, itemCenter));
            return new Popper(states, n);
        }

        /// <summary>
        /// Draws an option's icon and label centered on <paramref name="at"/>, sized for the frame's density.
        /// Micro wheels show short titles (file formats) as text and everything else as an icon.
        /// </summary>
        public static void DrawItemContent(DrawingContext dc, WheelFrame f, int index, Point at, double maxWidth,
                                           IBrush glyphBrush, IBrush labelBrush, Typeface? labelFace = null, double sizeBoost = 1.0)
        {
            var item = f.Items[index];
            string label = f.LabelOf(index);
            var face = labelFace ?? Face(f.Template.Shape.Font, FontWeight.SemiBold);
            double ts = f.TextScale * sizeBoost;

            if (f.Density == WheelDensity.Micro)
            {
                FormattedText t = label.Length <= 5 || string.IsNullOrEmpty(item.IconGlyph)
                    ? Centered(label, Face(f.Template.Shape.Font, FontWeight.Bold), 9.5 * ts, labelBrush, maxWidth, 1)
                    : Icon(item.IconGlyph, 14 * ts, glyphBrush);
                double w = t.MaxTextWidth > 1 && t.TextAlignment == TextAlignment.Center ? t.MaxTextWidth : t.Width;
                dc.DrawText(t, new Point(at.X - w / 2.0, at.Y - t.Height / 2.0));
                return;
            }

            bool compact = f.Density == WheelDensity.Compact;
            var glyph = Icon(item.IconGlyph, (compact ? 17 : 19) * ts, glyphBrush);
            var text = Centered(label, face, (compact ? 10.5 : 11.5) * ts, labelBrush, maxWidth, 2);

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
            public IBrush Title { get; init; } = Brushes.White;
            public IBrush Body { get; init; } = Brushes.Gray;
            public IBrush Faint { get; init; } = Brushes.Gray;
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
            double cx = f.Center.X, cy = f.Center.Y;
            var titleFace = style.TitleFace ?? Face(DisplayFont, FontWeight.SemiBold);
            var bodyFace = style.BodyFace ?? Face(UiFont, FontWeight.Normal);
            var badgeFace = style.BadgeFace ?? Face(UiFont, FontWeight.Bold);
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
                    lines.Add((Centered(title, titleFace, 9.5 * ts, style.Title, hubRadius * 1.8, 2), hubRadius * 1.8, 0));
                }
                else
                {
                    lines.Add((Icon(item.IconGlyph, (compact ? 18 : 22) * ts, accentBrush), 0, 0));
                    lines.Add((Centered(title, titleFace, (compact ? 12 : 14) * ts, style.Title, hubRadius * 1.7, compact ? 2 : 1), hubRadius * 1.7, 7 * s));
                    if (!compact && !string.IsNullOrEmpty(item.Description))
                        lines.Add((Centered(item.Description, bodyFace, 10.5 * ts, style.Body, hubRadius * 1.5, 3), hubRadius * 1.5, 4 * s));
                }
                DrawStack(dc, cx, cy, lines);
                return;
            }

            if (!string.IsNullOrEmpty(f.CenterBadge))
            {
                var badge = Text(f.CenterBadge, badgeFace, (micro ? 8.5 : 10) * ts, accentBrush);
                double padX = (micro ? 5 : 7) * s, padY = 2.5 * s;
                double pillW = badge.Width + padX * 2, pillH = badge.Height + padY * 2;

                if (micro)
                {
                    var microPill = new Rect(cx - pillW / 2.0, cy - pillH / 2.0, pillW, pillH);
                    dc.DrawRectangle(Solid(ColorUtil.WithAlpha(style.Accent, (byte)0x26)), null, microPill, 4 * s, 4 * s);
                    dc.DrawText(badge, new Point(microPill.X + padX, microPill.Y + padY));
                    return;
                }

                var name = Centered(f.CenterSubtitle, titleFace, (compact ? 11.5 : 13) * ts, style.Title, hubRadius * 1.7, compact ? 1 : 2);
                var hint = Centered(compact ? string.Empty : f.CenterTitle, bodyFace, 11 * ts, style.Faint, hubRadius * 1.6, 1);
                double total = pillH + 8 * s + name.Height + 4 * s + hint.Height;
                double y = cy - total / 2.0;

                var pill = new Rect(cx - pillW / 2.0, y, pillW, pillH);
                dc.DrawRectangle(Solid(ColorUtil.WithAlpha(style.Accent, (byte)0x26)), null, pill, 4 * s, 4 * s);
                dc.DrawText(badge, new Point(pill.X + padX, pill.Y + padY));
                y += pillH + 8 * s;
                dc.DrawText(name, new Point(cx - name.MaxTextWidth / 2.0, y));
                y += name.Height + 4 * s;
                dc.DrawText(hint, new Point(cx - hint.MaxTextWidth / 2.0, y));
                return;
            }

            var msg = new List<(FormattedText, double, double)>
            {
                (Icon(f.CenterGlyph, (micro ? 15 : compact ? 18 : 22) * ts, style.Faint), 0, 0)
            };
            if (!micro)
            {
                msg.Add((Centered(f.CenterTitle, titleFace, (compact ? 11.5 : 13) * ts, style.Title, hubRadius * 1.7, 2), hubRadius * 1.7, 7 * s));
                if (!compact && !string.IsNullOrEmpty(f.CenterSubtitle))
                    msg.Add((Centered(f.CenterSubtitle, bodyFace, 11 * ts, style.Faint, hubRadius * 1.6, 2), hubRadius * 1.6, 3 * s));
            }
            DrawStack(dc, cx, cy, msg);
        }

        // ------------------------------------------------------------------ brushes

        /// <summary>Radial gradient filling a circle's bounding box (WPF's default mapping): origin and center are relative, radius 0.5 = the circle.</summary>
        public static IBrush Radial(Point center, Point origin, double radius, params (Color Color, double Offset)[] stops)
        {
            var b = new RadialGradientBrush
            {
                Center = new RelativePoint(center, RelativeUnit.Relative),
                GradientOrigin = new RelativePoint(origin, RelativeUnit.Relative),
                RadiusX = new RelativeScalar(radius, RelativeUnit.Relative),
                RadiusY = new RelativeScalar(radius, RelativeUnit.Relative)
            };
            foreach (var (c, o) in stops) b.GradientStops.Add(new GradientStop(c, o));
            return b.ToImmutable();
        }

        /// <summary>Radial gradient centered on the bounding box with radius 0.5 (WPF's default RadialGradientBrush).</summary>
        public static IBrush Radial(params (Color Color, double Offset)[] stops) => Radial(new Point(0.5, 0.5), new Point(0.5, 0.5), 0.5, stops);

        /// <summary>Radial gradient in absolute coordinates.</summary>
        public static IBrush RadialAbsolute(Point center, double radius, params (Color Color, double Offset)[] stops)
        {
            var b = new RadialGradientBrush
            {
                Center = new RelativePoint(center, RelativeUnit.Absolute),
                GradientOrigin = new RelativePoint(center, RelativeUnit.Absolute),
                RadiusX = new RelativeScalar(radius, RelativeUnit.Absolute),
                RadiusY = new RelativeScalar(radius, RelativeUnit.Absolute)
            };
            foreach (var (c, o) in stops) b.GradientStops.Add(new GradientStop(c, o));
            return b.ToImmutable();
        }

        /// <summary>Linear gradient between two points, relative to the shape's bounding box or absolute.</summary>
        public static IBrush Linear(Point start, Point end, RelativeUnit unit, params (Color Color, double Offset)[] stops)
        {
            var b = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(start, unit),
                EndPoint = new RelativePoint(end, unit)
            };
            foreach (var (c, o) in stops) b.GradientStops.Add(new GradientStop(c, o));
            return b.ToImmutable();
        }

        /// <summary>Soft round shadow under the wheel.</summary>
        public static void DrawShadow(DrawingContext dc, Point center, double radius, double spread, double offsetY, Color shadow)
        {
            if (shadow.A == 0) return;
            double r = radius + spread;
            var brush = Radial((shadow, Math.Clamp((radius - spread) / r, 0, 1)), (ColorUtil.WithAlpha(shadow, (byte)0), 1.0));
            dc.DrawEllipse(brush, null, new Point(center.X, center.Y + offsetY), r, r);
        }
    }
}
