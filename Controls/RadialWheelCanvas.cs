using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using OgConverter.Models;

namespace OgConverter.Controls
{
    public class RadialWheelCanvas : FrameworkElement
    {
        public static readonly DependencyProperty HoveredIndexProperty =
            DependencyProperty.Register(nameof(HoveredIndex), typeof(int), typeof(RadialWheelCanvas),
                new FrameworkPropertyMetadata(-1, FrameworkPropertyMetadataOptions.AffectsRender, OnHoveredIndexChanged));

        public static readonly DependencyProperty CenterTitleProperty =
            DependencyProperty.Register(nameof(CenterTitle), typeof(string), typeof(RadialWheelCanvas),
                new FrameworkPropertyMetadata("Drag to format", FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty CenterSubtitleProperty =
            DependencyProperty.Register(nameof(CenterSubtitle), typeof(string), typeof(RadialWheelCanvas),
                new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty CenterGlyphProperty =
            DependencyProperty.Register(nameof(CenterGlyph), typeof(string), typeof(RadialWheelCanvas),
                new FrameworkPropertyMetadata("\uE8E5", FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty CenterBadgeProperty =
            DependencyProperty.Register(nameof(CenterBadge), typeof(string), typeof(RadialWheelCanvas),
                new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty CurrentModeProperty =
            DependencyProperty.Register(nameof(CurrentMode), typeof(WheelMode), typeof(RadialWheelCanvas),
                new FrameworkPropertyMetadata(WheelMode.Convert, FrameworkPropertyMetadataOptions.AffectsRender, OnCurrentModeChanged));

        public static readonly DependencyProperty WheelScaleProperty =
            DependencyProperty.Register(nameof(WheelScale), typeof(double), typeof(RadialWheelCanvas),
                new FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.AffectsRender, OnWheelScaleChanged));

        private static void OnHoveredIndexChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            int oldVal = (int)e.OldValue;
            int newVal = (int)e.NewValue;
            if (newVal >= 0 && newVal != oldVal)
            {
                Services.SoundService.Instance.PlayTick();
            }
        }

        private static void OnCurrentModeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (!Equals(e.OldValue, e.NewValue))
            {
                Services.SoundService.Instance.PlaySwitch();
            }
        }

        private static void OnWheelScaleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is RadialWheelCanvas canvas)
            {
                canvas.UpdateRadii();
            }
        }

        public int HoveredIndex
        {
            get => (int)GetValue(HoveredIndexProperty);
            set => SetValue(HoveredIndexProperty, value);
        }

        /// <summary>Hub instruction line (idle) or headline when no file is loaded.</summary>
        public string CenterTitle
        {
            get => (string)GetValue(CenterTitleProperty);
            set => SetValue(CenterTitleProperty, value);
        }

        /// <summary>Hub file name (idle).</summary>
        public string CenterSubtitle
        {
            get => (string)GetValue(CenterSubtitleProperty);
            set => SetValue(CenterSubtitleProperty, value);
        }

        /// <summary>Fluent icon glyph shown in the hub when no file badge is set.</summary>
        public string CenterGlyph
        {
            get => (string)GetValue(CenterGlyphProperty);
            set => SetValue(CenterGlyphProperty, value);
        }

        /// <summary>Short file-type label shown as a pill in the hub (e.g. "PDF"). Empty hides it.</summary>
        public string CenterBadge
        {
            get => (string)GetValue(CenterBadgeProperty);
            set => SetValue(CenterBadgeProperty, value);
        }

        public WheelMode CurrentMode
        {
            get => (WheelMode)GetValue(CurrentModeProperty);
            set => SetValue(CurrentModeProperty, value);
        }

        public double WheelScale
        {
            get => (double)GetValue(WheelScaleProperty);
            set => SetValue(WheelScaleProperty, value);
        }

        public double InnerRadius { get; set; } = 76;
        public double OuterRadius { get; set; } = 188;

        public void UpdateRadii()
        {
            double scale = Math.Clamp(WheelScale, 0.6, 1.5);
            InnerRadius = 76.0 * scale;
            OuterRadius = 188.0 * scale;
            InvalidateVisual();
        }

        private List<WheelItem> _items = new();
        public List<WheelItem> Items
        {
            get => _items;
            set
            {
                _items = value ?? new List<WheelItem>();
                InvalidateVisual();
            }
        }

        /// <summary>Shows a file in the hub: type badge, file name and an instruction line.</summary>
        public void SetFile(string path, string instruction)
        {
            bool isFolder = System.IO.Directory.Exists(path);
            string ext = System.IO.Path.GetExtension(path).TrimStart('.').ToUpperInvariant();
            CenterBadge = isFolder ? "FOLDER" : (ext.Length is > 0 and <= 6 ? ext : "FILE");
            CenterSubtitle = System.IO.Path.GetFileName(path.TrimEnd('\\', '/'));
            CenterTitle = instruction;
            CenterGlyph = Services.FormatRegistry.GetCategoryGlyph(path);
        }

        /// <summary>Clears the file and shows a glyph + headline + hint instead.</summary>
        public void SetMessage(string glyph, string title, string hint)
        {
            CenterBadge = string.Empty;
            CenterGlyph = glyph;
            CenterTitle = title;
            CenterSubtitle = hint;
        }

        public static Color AccentFor(WheelMode mode) => mode switch
        {
            WheelMode.Tools => Color.FromRgb(0x3B, 0x9E, 0xFF),
            WheelMode.Actions => Color.FromRgb(0xA7, 0x8B, 0xFA),
            _ => Color.FromRgb(0xFF, 0x6B, 0x35)
        };

        public int HitTestSlice(Point position)
        {
            if (_items.Count == 0) return -1;

            double cx = ActualWidth / 2.0;
            double cy = ActualHeight / 2.0;

            double dx = position.X - cx;
            double dy = position.Y - cy;
            double dist = Math.Sqrt(dx * dx + dy * dy);

            // Within annular ring
            if (dist < InnerRadius - 10 || dist > OuterRadius + 30)
            {
                return -1;
            }

            // Atan2: 0 is Right (+X), 90 is Down (+Y), -90 is Up (-Y)
            double deg = Math.Atan2(dy, dx) * 180.0 / Math.PI;
            if (deg < 0) deg += 360.0;

            // Rotate so top (270 deg / -90 deg) is 0
            deg = (deg + 90.0) % 360.0;

            double sliceAngle = 360.0 / _items.Count;
            int idx = (int)(deg / sliceAngle);

            if (idx >= 0 && idx < _items.Count)
            {
                return idx;
            }

            return -1;
        }

        // ------------------------------------------------------------------ rendering

        private static readonly FontFamily IconFamily = new("Segoe Fluent Icons, Segoe MDL2 Assets, Segoe UI Symbol");
        private static readonly Typeface IconFace = new(IconFamily, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        private static readonly Typeface LabelFace = new(new FontFamily("Segoe UI Variable Text, Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
        private static readonly Typeface TitleFace = new(new FontFamily("Segoe UI Variable Display, Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
        private static readonly Typeface BodyFace = new(new FontFamily("Segoe UI Variable Text, Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        private static readonly Typeface BadgeFace = new(new FontFamily("Segoe UI Variable Text, Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);

        private static readonly Brush RingBrush = Frozen(new SolidColorBrush(Color.FromArgb(0xF5, 0x15, 0x17, 0x1C)));
        private static readonly Brush HubBrush = Frozen(new SolidColorBrush(Color.FromRgb(0x1B, 0x1D, 0x23)));
        private static readonly Brush TextPrimary = Frozen(new SolidColorBrush(Color.FromRgb(0xEC, 0xED, 0xEF)));
        private static readonly Brush TextSecondary = Frozen(new SolidColorBrush(Color.FromRgb(0xA1, 0xA6, 0xB0)));
        private static readonly Brush TextTertiary = Frozen(new SolidColorBrush(Color.FromRgb(0x7A, 0x7F, 0x8A)));
        private static readonly Pen OuterEdgePen = Frozen(new Pen(new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF)), 1));
        private static readonly Pen InnerEdgePen = Frozen(new Pen(new SolidColorBrush(Color.FromArgb(0x18, 0xFF, 0xFF, 0xFF)), 1));
        private static readonly Pen DividerPen = Frozen(new Pen(new SolidColorBrush(Color.FromArgb(0x16, 0xFF, 0xFF, 0xFF)), 1));

        private static T Frozen<T>(T f) where T : Freezable { f.Freeze(); return f; }

        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);
            if (ActualWidth < 10 || ActualHeight < 10) return;

            double s = WheelScale;
            double ts = Math.Max(s, 0.85);   // text scale: floor keeps labels readable on small wheels
            bool compact = s < 0.8;          // small hub: drop secondary lines
            double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
            double cx = ActualWidth / 2.0;
            double cy = ActualHeight / 2.0;
            var center = new Point(cx, cy);
            Color accent = AccentFor(CurrentMode);
            Brush accentBrush = Frozen(new SolidColorBrush(accent));

            // 1. Soft drop shadow so the wheel reads on light and dark desktops alike
            double shadowR = OuterRadius + 26 * s;
            var shadow = new RadialGradientBrush { Center = new Point(0.5, 0.5), GradientOrigin = new Point(0.5, 0.5), RadiusX = 0.5, RadiusY = 0.5 };
            shadow.GradientStops.Add(new GradientStop(Color.FromArgb(0x70, 0, 0, 0), (OuterRadius - 24 * s) / shadowR));
            shadow.GradientStops.Add(new GradientStop(Color.FromArgb(0x00, 0, 0, 0), 1.0));
            shadow.Freeze();
            dc.DrawEllipse(shadow, null, new Point(cx, cy + 6 * s), shadowR, shadowR);

            // 2. Ring body: one seamless surface with a faint top sheen
            dc.DrawEllipse(RingBrush, OuterEdgePen, center, OuterRadius, OuterRadius);
            var sheen = new LinearGradientBrush { StartPoint = new Point(0.5, 0), EndPoint = new Point(0.5, 0.55) };
            sheen.GradientStops.Add(new GradientStop(Color.FromArgb(0x0E, 0xFF, 0xFF, 0xFF), 0));
            sheen.GradientStops.Add(new GradientStop(Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF), 1));
            sheen.Freeze();
            dc.DrawEllipse(sheen, null, center, OuterRadius, OuterRadius);

            int count = _items.Count;
            double sliceAngle = count > 0 ? 360.0 / count : 360.0;

            // 3. Hairline dividers between options
            if (count > 1)
            {
                for (int i = 0; i < count; i++)
                {
                    double rad = (i * sliceAngle - 90.0) * Math.PI / 180.0;
                    var a = new Point(cx + (InnerRadius + 6 * s) * Math.Cos(rad), cy + (InnerRadius + 6 * s) * Math.Sin(rad));
                    var b = new Point(cx + (OuterRadius - 6 * s) * Math.Cos(rad), cy + (OuterRadius - 6 * s) * Math.Sin(rad));
                    dc.DrawLine(DividerPen, a, b);
                }
            }

            // 4. Hovered option: tinted sector, slight lift, accent edge
            int hovered = HoveredIndex;
            if (hovered >= 0 && hovered < count)
            {
                double start = hovered * sliceAngle - 90.0;
                double lift = 6 * s;
                double rOut = OuterRadius + lift;
                var sector = CreateAnnularSector(center, InnerRadius + 2 * s, rOut, start + 0.6, sliceAngle - 1.2);

                var fill = new RadialGradientBrush
                {
                    MappingMode = BrushMappingMode.Absolute,
                    Center = center,
                    GradientOrigin = center,
                    RadiusX = rOut,
                    RadiusY = rOut
                };
                fill.GradientStops.Add(new GradientStop(Color.FromArgb(0x1E, accent.R, accent.G, accent.B), InnerRadius / rOut));
                fill.GradientStops.Add(new GradientStop(Color.FromArgb(0x50, accent.R, accent.G, accent.B), 1.0));
                fill.Freeze();
                var border = Frozen(new Pen(new SolidColorBrush(Color.FromArgb(0x60, accent.R, accent.G, accent.B)), 1));
                dc.DrawGeometry(fill, border, sector);

                // Accent arc along the outer edge
                var edge = Frozen(new Pen(accentBrush, 3 * s) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round });
                double inset = Math.Min(4, sliceAngle * 0.08);
                dc.DrawGeometry(null, edge, CreateArc(center, rOut - 1.5 * s, start + inset, sliceAngle - inset * 2));
            }

            // 5. Icons + labels
            for (int i = 0; i < count; i++)
            {
                bool isHovered = i == hovered;
                var item = _items[i];
                double midRad = (i * sliceAngle - 90.0 + sliceAngle / 2.0) * Math.PI / 180.0;
                double rMid = (InnerRadius + OuterRadius) / 2.0 + (isHovered ? 3 * s : 0);
                double tx = cx + rMid * Math.Cos(midRad);
                double ty = cy + rMid * Math.Sin(midRad);

                double chord = 2 * rMid * Math.Sin(Math.Min(sliceAngle, 180) * Math.PI / 360.0);
                double labelWidth = Math.Max(40 * s, Math.Min(chord * 0.86, (OuterRadius - InnerRadius) * 1.25));

                var glyph = new FormattedText(item.IconGlyph, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, IconFace,
                    (compact ? 17 : 19) * ts, isHovered ? accentBrush : TextSecondary, dpi);
                var label = CenteredText(item.Title, LabelFace, (compact ? 10.5 : 11.5) * ts, isHovered ? Brushes.White : TextPrimary, labelWidth, 2, dpi);

                double gap = 6 * s;
                double blockH = glyph.Height + gap + label.Height;
                double y = ty - blockH / 2.0;
                dc.DrawText(glyph, new Point(tx - glyph.Width / 2.0, y));
                dc.DrawText(label, new Point(tx - labelWidth / 2.0, y + glyph.Height + gap));
            }

            // 6. Hub
            double hubR = InnerRadius - 7 * s;
            dc.DrawEllipse(RingBrush, InnerEdgePen, center, InnerRadius, InnerRadius);
            var hubPen = Frozen(new Pen(new SolidColorBrush(Color.FromArgb(0x8C, accent.R, accent.G, accent.B)), 1.5));
            dc.DrawEllipse(HubBrush, hubPen, center, hubR, hubR);

            if (hovered >= 0 && hovered < count)
            {
                // Hovered: what this option does
                var item = _items[hovered];
                var lines = new List<(FormattedText Text, double Width, double GapBefore)>
                {
                    (new FormattedText(item.IconGlyph, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, IconFace, (compact ? 18 : 22) * ts, accentBrush, dpi), 0, 0),
                    (CenteredText(item.Title, TitleFace, (compact ? 12 : 14) * ts, Brushes.White, hubR * 1.7, compact ? 2 : 1, dpi), hubR * 1.7, 7 * s),
                };
                if (!compact && !string.IsNullOrEmpty(item.Description))
                {
                    lines.Add((CenteredText(item.Description, BodyFace, 10.5 * ts, TextSecondary, hubR * 1.5, 3, dpi), hubR * 1.5, 4 * s));
                }
                DrawStack(dc, cx, cy, lines);
            }
            else if (!string.IsNullOrEmpty(CenterBadge))
            {
                // Idle with a file: type pill, name, hint
                var badge = new FormattedText(CenterBadge, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, BadgeFace, 10 * ts, accentBrush, dpi);
                var name = CenteredText(CenterSubtitle, TitleFace, (compact ? 11.5 : 13) * ts, TextPrimary, hubR * 1.7, compact ? 1 : 2, dpi);
                var hint = CenteredText(compact ? string.Empty : CenterTitle, BodyFace, 11 * ts, TextTertiary, hubR * 1.6, 1, dpi);

                double padX = 7 * s, padY = 2.5 * s;
                double pillW = badge.Width + padX * 2, pillH = badge.Height + padY * 2;
                double total = pillH + 8 * s + name.Height + 4 * s + hint.Height;
                double y = cy - total / 2.0;

                var pill = new Rect(cx - pillW / 2.0, y, pillW, pillH);
                dc.DrawRoundedRectangle(Frozen(new SolidColorBrush(Color.FromArgb(0x26, accent.R, accent.G, accent.B))), null, pill, 4 * s, 4 * s);
                dc.DrawText(badge, new Point(pill.X + padX, pill.Y + padY));
                y += pillH + 8 * s;
                dc.DrawText(name, new Point(cx - name.MaxTextWidth / 2.0, y));
                y += name.Height + 4 * s;
                dc.DrawText(hint, new Point(cx - hint.MaxTextWidth / 2.0, y));
            }
            else
            {
                // No file: icon, headline, hint
                var lines = new List<(FormattedText Text, double Width, double GapBefore)>
                {
                    (new FormattedText(CenterGlyph, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, IconFace, (compact ? 18 : 22) * ts, TextTertiary, dpi), 0, 0),
                    (CenteredText(CenterTitle, TitleFace, (compact ? 11.5 : 13) * ts, TextPrimary, hubR * 1.7, 2, dpi), hubR * 1.7, 7 * s),
                };
                if (!compact && !string.IsNullOrEmpty(CenterSubtitle))
                {
                    lines.Add((CenteredText(CenterSubtitle, BodyFace, 11 * ts, TextTertiary, hubR * 1.6, 2, dpi), hubR * 1.6, 3 * s));
                }
                DrawStack(dc, cx, cy, lines);
            }
        }

        private static FormattedText CenteredText(string text, Typeface face, double size, Brush brush, double maxWidth, int maxLines, double dpi) =>
            new(text ?? string.Empty, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, face, size, brush, dpi)
            {
                MaxTextWidth = Math.Max(1, maxWidth),
                MaxLineCount = maxLines,
                Trimming = TextTrimming.CharacterEllipsis,
                TextAlignment = TextAlignment.Center
            };

        /// <summary>Draws lines vertically centered on (cx, cy). Width 0 means the text's natural width.</summary>
        private static void DrawStack(DrawingContext dc, double cx, double cy, List<(FormattedText Text, double Width, double GapBefore)> lines)
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

        private static Geometry CreateArc(Point center, double r, double startDeg, double sweepDeg)
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

        private static Geometry CreateAnnularSector(Point center, double rInner, double rOuter, double startDeg, double sweepDeg)
        {
            if (sweepDeg >= 359.5)
            {
                // Full ring (single option): two 180-degree arcs avoid ArcTo start==end collapse
                var geom = new StreamGeometry();
                using (var ctx = geom.Open())
                {
                    Point pOuterTop = new Point(center.X, center.Y - rOuter);
                    Point pOuterBottom = new Point(center.X, center.Y + rOuter);
                    Point pInnerBottom = new Point(center.X, center.Y + rInner);
                    Point pInnerTop = new Point(center.X, center.Y - rInner);

                    ctx.BeginFigure(pOuterTop, true, true);
                    ctx.ArcTo(pOuterBottom, new Size(rOuter, rOuter), 0, false, SweepDirection.Clockwise, true, false);
                    ctx.ArcTo(pOuterTop, new Size(rOuter, rOuter), 0, false, SweepDirection.Clockwise, true, false);

                    ctx.BeginFigure(pInnerTop, true, true);
                    ctx.ArcTo(pInnerBottom, new Size(rInner, rInner), 0, false, SweepDirection.Counterclockwise, true, false);
                    ctx.ArcTo(pInnerTop, new Size(rInner, rInner), 0, false, SweepDirection.Counterclockwise, true, false);
                }
                geom.Freeze();
                return geom;
            }

            double endDeg = startDeg + sweepDeg;
            double startRad = startDeg * Math.PI / 180.0;
            double endRad = endDeg * Math.PI / 180.0;

            Point pOuterStart = new Point(center.X + (rOuter * Math.Cos(startRad)), center.Y + (rOuter * Math.Sin(startRad)));
            Point pOuterEnd = new Point(center.X + (rOuter * Math.Cos(endRad)), center.Y + (rOuter * Math.Sin(endRad)));
            Point pInnerEnd = new Point(center.X + (rInner * Math.Cos(endRad)), center.Y + (rInner * Math.Sin(endRad)));
            Point pInnerStart = new Point(center.X + (rInner * Math.Cos(startRad)), center.Y + (rInner * Math.Sin(startRad)));

            bool isLargeArc = sweepDeg > 180.0;

            var streamGeom = new StreamGeometry();
            using (var ctx = streamGeom.Open())
            {
                ctx.BeginFigure(pInnerStart, true, true);
                ctx.LineTo(pOuterStart, true, false);
                ctx.ArcTo(pOuterEnd, new Size(rOuter, rOuter), 0, isLargeArc, SweepDirection.Clockwise, true, false);
                ctx.LineTo(pInnerEnd, true, false);
                ctx.ArcTo(pInnerStart, new Size(rInner, rInner), 0, isLargeArc, SweepDirection.Counterclockwise, true, false);
            }

            streamGeom.Freeze();
            return streamGeom;
        }
    }
}
