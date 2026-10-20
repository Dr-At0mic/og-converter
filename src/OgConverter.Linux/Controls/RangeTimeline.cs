using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using OgConverter.Themes;

namespace OgConverter.Controls
{
    /// <summary>
    /// A timeline with a start and an end handle. Drag a handle to move that end, drag the highlighted part to
    /// slide the whole range, or click the track to move the nearest handle there.
    /// </summary>
    public class RangeTimeline : Control
    {
        private const double Pad = 10;
        private const double TrackHeight = 8;
        private const double HandleWidth = 10;
        private const double HandleHeight = 26;

        private enum Part { None, Start, End, Range }

        private double _duration = 1, _start, _end = 1;
        private Part _drag = Part.None;
        private double _grabOffset;

        private static readonly IPen Grip = new ImmutablePen(Brushes.White.ToImmutable(), 1);

        public event EventHandler? RangeChanged;

        public RangeTimeline()
        {
            Height = 40;
            Focusable = true;
            Cursor = new Cursor(StandardCursorType.Hand);
        }

        public double Duration
        {
            get => _duration;
            set
            {
                _duration = Math.Max(0.1, value);
                SetRange(_start, Math.Min(_end, _duration));
            }
        }

        public double Start => _start;
        public double End => _end;

        /// <summary>Smallest range the handles can make, in seconds.</summary>
        public double MinLength { get; set; } = 0.1;

        public void SetRange(double start, double end)
        {
            start = Math.Clamp(start, 0, _duration);
            end = Math.Clamp(end, 0, _duration);
            if (end - start < MinLength)
            {
                if (start + MinLength <= _duration) end = start + MinLength;
                else start = Math.Max(0, end - MinLength);
            }
            _start = start;
            _end = end;
            InvalidateVisual();
            RangeChanged?.Invoke(this, EventArgs.Empty);
        }

        private double TrackWidth => Math.Max(1, Bounds.Width - 2 * Pad);
        private double X(double t) => Pad + t / _duration * TrackWidth;
        private double T(double x) => Math.Clamp((x - Pad) / TrackWidth * _duration, 0, _duration);

        public override void Render(DrawingContext dc)
        {
            dc.DrawRectangle(Brushes.Transparent, null, new Rect(Bounds.Size));
            double cy = Bounds.Height / 2;

            var track = AppThemeService.Brush("BorderStrongBrush");
            var accent = AppThemeService.Brush("AccentBrush");
            var subtle = AppThemeService.Brush("AccentSubtleBrush");

            dc.DrawRectangle(track, null, new Rect(Pad, cy - TrackHeight / 2, TrackWidth, TrackHeight), 4, 4);

            double xs = X(_start), xe = X(_end);
            dc.DrawRectangle(subtle, null, new Rect(xs, cy - HandleHeight / 2, Math.Max(0, xe - xs), HandleHeight));
            dc.DrawRectangle(accent, null, new Rect(xs, cy - TrackHeight / 2, Math.Max(0, xe - xs), TrackHeight));

            foreach (double x in new[] { xs, xe })
            {
                dc.DrawRectangle(accent, null, new Rect(x - HandleWidth / 2, cy - HandleHeight / 2, HandleWidth, HandleHeight), 3, 3);
                dc.DrawLine(Grip, new Point(x - 1.5, cy - 5), new Point(x - 1.5, cy + 5));
                dc.DrawLine(Grip, new Point(x + 1.5, cy - 5), new Point(x + 1.5, cy + 5));
            }
        }

        private Part HitTest(double x)
        {
            double xs = X(_start), xe = X(_end);
            bool nearStart = Math.Abs(x - xs) <= HandleWidth, nearEnd = Math.Abs(x - xe) <= HandleWidth;
            // When both handles overlap, pick the one the pointer is on the outer side of
            if (nearStart && nearEnd) return x < (xs + xe) / 2 ? Part.Start : Part.End;
            if (nearStart) return Part.Start;
            if (nearEnd) return Part.End;
            if (x > xs && x < xe) return Part.Range;
            return Part.None;
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
            Focus();
            double x = e.GetPosition(this).X;
            _drag = HitTest(x);

            if (_drag == Part.None)
            {
                // Click on the track: bring the nearer handle there
                double t = T(x);
                _drag = Math.Abs(t - _start) < Math.Abs(t - _end) ? Part.Start : Part.End;
                Move(t);
            }
            _grabOffset = T(x) - _start;
            e.Pointer.Capture(this);
            e.Handled = true;
        }

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            base.OnPointerMoved(e);
            double x = e.GetPosition(this).X;
            if (_drag == Part.None)
            {
                Cursor = new Cursor(HitTest(x) switch { Part.Range => StandardCursorType.SizeAll, Part.None => StandardCursorType.Hand, _ => StandardCursorType.SizeWestEast });
                return;
            }
            Move(T(x));
        }

        private void Move(double t)
        {
            switch (_drag)
            {
                case Part.Start:
                    SetRange(Math.Min(t, _end - MinLength), _end);
                    break;
                case Part.End:
                    SetRange(_start, Math.Max(t, _start + MinLength));
                    break;
                case Part.Range:
                    double length = _end - _start;
                    double s = Math.Clamp(t - _grabOffset, 0, _duration - length);
                    SetRange(s, s + length);
                    break;
            }
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            base.OnPointerReleased(e);
            _drag = Part.None;
            e.Pointer.Capture(null);
        }

        // The colors follow the wheel template
        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            AppThemeService.Applied += InvalidateVisual;
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnDetachedFromVisualTree(e);
            AppThemeService.Applied -= InvalidateVisual;
        }
    }
}
