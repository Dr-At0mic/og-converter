using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace OgConverter.Controls
{
    /// <summary>
    /// A timeline with a start and an end handle. Drag a handle to move that end, drag the highlighted part to
    /// slide the whole range, or click the track to move the nearest handle there.
    /// </summary>
    public class RangeTimeline : FrameworkElement
    {
        private const double Pad = 10;
        private const double TrackHeight = 8;
        private const double HandleWidth = 10;
        private const double HandleHeight = 26;

        private enum Part { None, Start, End, Range }

        private double _duration = 1, _start, _end = 1;
        private Part _drag = Part.None;
        private double _grabOffset;

        public event EventHandler? RangeChanged;

        public RangeTimeline()
        {
            Height = 40;
            Focusable = true;
            Cursor = Cursors.Hand;
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

        private double TrackWidth => Math.Max(1, ActualWidth - 2 * Pad);
        private double X(double t) => Pad + t / _duration * TrackWidth;
        private double T(double x) => Math.Clamp((x - Pad) / TrackWidth * _duration, 0, _duration);

        protected override void OnRender(DrawingContext dc)
        {
            dc.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));
            double cy = ActualHeight / 2;

            var track = (Brush)FindResource("BorderStrongBrush");
            var accent = (Brush)FindResource("AccentBrush");
            var subtle = (Brush)FindResource("AccentSubtleBrush");

            dc.DrawRoundedRectangle(track, null, new Rect(Pad, cy - TrackHeight / 2, TrackWidth, TrackHeight), 4, 4);

            double xs = X(_start), xe = X(_end);
            dc.DrawRectangle(subtle, null, new Rect(xs, cy - HandleHeight / 2, Math.Max(0, xe - xs), HandleHeight));
            dc.DrawRectangle(accent, null, new Rect(xs, cy - TrackHeight / 2, Math.Max(0, xe - xs), TrackHeight));

            foreach (double x in new[] { xs, xe })
            {
                dc.DrawRoundedRectangle(accent, null, new Rect(x - HandleWidth / 2, cy - HandleHeight / 2, HandleWidth, HandleHeight), 3, 3);
                var grip = new Pen(Brushes.White, 1);
                dc.DrawLine(grip, new Point(x - 1.5, cy - 5), new Point(x - 1.5, cy + 5));
                dc.DrawLine(grip, new Point(x + 1.5, cy - 5), new Point(x + 1.5, cy + 5));
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

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e);
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
            CaptureMouse();
            e.Handled = true;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            double x = e.GetPosition(this).X;
            if (_drag == Part.None)
            {
                Cursor = HitTest(x) switch { Part.Range => Cursors.SizeAll, Part.None => Cursors.Hand, _ => Cursors.SizeWE };
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

        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonUp(e);
            _drag = Part.None;
            ReleaseMouseCapture();
        }
    }
}
