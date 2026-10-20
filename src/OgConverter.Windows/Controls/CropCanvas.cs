using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace OgConverter.Controls
{
    /// <summary>
    /// Shows an image fitted to the control with a crop rectangle on top: drag inside to move it, drag a handle
    /// to resize it, drag outside it to draw a new one. The selection is kept in image pixels.
    /// </summary>
    public class CropCanvas : FrameworkElement
    {
        private const double Pad = 14;
        private const double HandleHit = 12;
        private const double MinSize = 8;

        private enum Hit { None, Move, N, S, E, W, NE, NW, SE, SW, Draw }

        private BitmapSource? _source;
        private Rect _sel;
        private double? _aspect;

        private Hit _drag = Hit.None;
        private Point _dragStart;
        private Rect _selAtStart;
        private Point _anchor;

        private static readonly Brush Dim = Frozen(new SolidColorBrush(Color.FromArgb(0x99, 0, 0, 0)));
        private static readonly Pen Border = Frozen(new Pen(Brushes.White, 1.5));
        private static readonly Pen Thirds = Frozen(new Pen(new SolidColorBrush(Color.FromArgb(0x70, 255, 255, 255)), 1));
        private static readonly Pen HandleOutline = Frozen(new Pen(new SolidColorBrush(Color.FromArgb(0x60, 0, 0, 0)), 1));
        private static readonly Brush Checker = CreateChecker();

        public event EventHandler? SelectionChanged;

        public CropCanvas()
        {
            Focusable = true;
            ClipToBounds = true;
            RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.HighQuality);
        }

        public BitmapSource? Source
        {
            get => _source;
            set
            {
                _source = value;
                _aspect = null;
                Reset();
            }
        }

        /// <summary>Shows a different picture of the same size (another video frame) and keeps the selection.</summary>
        public void ReplaceFrame(BitmapSource frame)
        {
            if (_source == null || frame.PixelWidth != ImageWidth || frame.PixelHeight != ImageHeight)
            {
                Source = frame;
                return;
            }
            _source = frame;
            InvalidateVisual();
        }

        public int ImageWidth => _source?.PixelWidth ?? 0;
        public int ImageHeight => _source?.PixelHeight ?? 0;

        /// <summary>Width ÷ height to lock to, or null for a free selection. Setting it fits the largest such area, centred.</summary>
        public double? AspectRatio
        {
            get => _aspect;
            set
            {
                _aspect = value;
                if (_source == null) return;
                if (value is not double a)
                {
                    Changed();
                    return;
                }

                double w = ImageWidth, h = w / a;
                if (h > ImageHeight) { h = ImageHeight; w = h * a; }
                SetSelection(new Rect((ImageWidth - w) / 2, (ImageHeight - h) / 2, w, h));
            }
        }

        /// <summary>The selection rounded to whole pixels inside the image.</summary>
        public Int32Rect PixelSelection
        {
            get
            {
                if (_source == null) return Int32Rect.Empty;
                int x = (int)Math.Round(_sel.X), y = (int)Math.Round(_sel.Y);
                int w = Math.Min((int)Math.Round(_sel.Width), ImageWidth - x);
                int h = Math.Min((int)Math.Round(_sel.Height), ImageHeight - y);
                return new Int32Rect(x, y, Math.Max(1, w), Math.Max(1, h));
            }
        }

        public bool IsWholeImage => PixelSelection is { X: 0, Y: 0 } p && p.Width == ImageWidth && p.Height == ImageHeight;

        public void Reset()
        {
            _sel = _source == null ? Rect.Empty : new Rect(0, 0, ImageWidth, ImageHeight);
            if (_aspect != null) AspectRatio = _aspect;
            else Changed();
        }

        /// <summary>Selection in image pixels (clamped to the image).</summary>
        public void SetSelection(Rect r)
        {
            if (_source == null) return;
            double w = Math.Clamp(r.Width, 1, ImageWidth), h = Math.Clamp(r.Height, 1, ImageHeight);
            _sel = new Rect(Math.Clamp(r.X, 0, ImageWidth - w), Math.Clamp(r.Y, 0, ImageHeight - h), w, h);
            Changed();
        }

        /// <summary>Moves the selection by whole image pixels (arrow keys).</summary>
        public void Nudge(double dx, double dy) => SetSelection(new Rect(_sel.X + dx, _sel.Y + dy, _sel.Width, _sel.Height));

        private void Changed()
        {
            InvalidateVisual();
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }

        // ---- Layout -----------------------------------------------------------------------------------------

        private double Scale => _source == null ? 1
            : Math.Max(0.0001, Math.Min(Math.Min((ActualWidth - 2 * Pad) / ImageWidth, (ActualHeight - 2 * Pad) / ImageHeight), 8));

        private Rect ImageRect
        {
            get
            {
                double s = Scale, w = ImageWidth * s, h = ImageHeight * s;
                return new Rect((ActualWidth - w) / 2, (ActualHeight - h) / 2, w, h);
            }
        }

        private Rect ToScreen(Rect r)
        {
            var img = ImageRect;
            double s = Scale;
            return new Rect(img.X + r.X * s, img.Y + r.Y * s, r.Width * s, r.Height * s);
        }

        private Point ToImage(Point p)
        {
            var img = ImageRect;
            double s = Scale;
            return new Point(Math.Clamp((p.X - img.X) / s, 0, ImageWidth), Math.Clamp((p.Y - img.Y) / s, 0, ImageHeight));
        }

        // ---- Rendering --------------------------------------------------------------------------------------

        protected override void OnRender(DrawingContext dc)
        {
            dc.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));
            if (_source == null) return;

            var img = ImageRect;
            dc.DrawRectangle(Checker, null, img);
            dc.DrawImage(_source, img);

            var sel = ToScreen(_sel);
            var outside = new GeometryGroup { FillRule = FillRule.EvenOdd };
            outside.Children.Add(new RectangleGeometry(img));
            outside.Children.Add(new RectangleGeometry(sel));
            dc.DrawGeometry(Dim, null, outside);

            // Rule-of-thirds guides
            for (int i = 1; i <= 2; i++)
            {
                double x = sel.X + sel.Width * i / 3, y = sel.Y + sel.Height * i / 3;
                dc.DrawLine(Thirds, new Point(x, sel.Top), new Point(x, sel.Bottom));
                dc.DrawLine(Thirds, new Point(sel.Left, y), new Point(sel.Right, y));
            }
            dc.DrawRectangle(null, Border, sel);

            foreach (var (_, pt) in Handles(sel))
            {
                dc.DrawRectangle(Brushes.White, HandleOutline, new Rect(pt.X - 5, pt.Y - 5, 10, 10));
            }
        }

        private static (Hit Hit, Point Point)[] Handles(Rect s) => new[]
        {
            (Hit.NW, s.TopLeft), (Hit.NE, s.TopRight), (Hit.SW, s.BottomLeft), (Hit.SE, s.BottomRight),
            (Hit.N, new Point(s.X + s.Width / 2, s.Top)), (Hit.S, new Point(s.X + s.Width / 2, s.Bottom)),
            (Hit.W, new Point(s.Left, s.Y + s.Height / 2)), (Hit.E, new Point(s.Right, s.Y + s.Height / 2))
        };

        // ---- Mouse ------------------------------------------------------------------------------------------

        private Hit HitTest(Point p)
        {
            var sel = ToScreen(_sel);
            foreach (var (hit, pt) in Handles(sel))
            {
                if (Math.Abs(p.X - pt.X) <= HandleHit && Math.Abs(p.Y - pt.Y) <= HandleHit) return hit;
            }
            if (sel.Contains(p)) return Hit.Move;
            return ImageRect.Contains(p) ? Hit.Draw : Hit.None;
        }

        private static Cursor CursorFor(Hit hit) => hit switch
        {
            Hit.Move => Cursors.SizeAll,
            Hit.N or Hit.S => Cursors.SizeNS,
            Hit.E or Hit.W => Cursors.SizeWE,
            Hit.NE or Hit.SW => Cursors.SizeNESW,
            Hit.NW or Hit.SE => Cursors.SizeNWSE,
            Hit.Draw => Cursors.Cross,
            _ => Cursors.Arrow
        };

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e);
            if (_source == null) return;
            Focus();

            var p = e.GetPosition(this);
            _drag = HitTest(p);
            if (_drag == Hit.None) return;

            _dragStart = ToImage(p);
            _selAtStart = _sel;
            _anchor = _drag switch
            {
                Hit.NW => _sel.BottomRight,
                Hit.NE => _sel.BottomLeft,
                Hit.SW => _sel.TopRight,
                Hit.SE => _sel.TopLeft,
                Hit.Draw => _dragStart,
                _ => default
            };
            CaptureMouse();
            e.Handled = true;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_source == null) return;

            var p = e.GetPosition(this);
            if (_drag == Hit.None)
            {
                Cursor = CursorFor(HitTest(p));
                return;
            }

            var q = ToImage(p);
            switch (_drag)
            {
                case Hit.Move:
                    SetSelection(new Rect(_selAtStart.X + q.X - _dragStart.X, _selAtStart.Y + q.Y - _dragStart.Y, _selAtStart.Width, _selAtStart.Height));
                    break;
                case Hit.NW or Hit.NE or Hit.SW or Hit.SE or Hit.Draw:
                    DragCorner(q);
                    break;
                default:
                    DragEdge(q);
                    break;
            }
        }

        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonUp(e);
            if (_drag == Hit.None) return;

            // A click without a drag outside the selection shouldn't collapse it to nothing
            if (_drag == Hit.Draw && (_sel.Width < MinSize || _sel.Height < MinSize)) SetSelection(_selAtStart);
            _drag = Hit.None;
            ReleaseMouseCapture();
        }

        /// <summary>The corner opposite the anchor follows the pointer (it can flip across the anchor).</summary>
        private void DragCorner(Point q)
        {
            double w = Math.Abs(q.X - _anchor.X), h = Math.Abs(q.Y - _anchor.Y);
            if (_aspect is double a)
            {
                if (w / Math.Max(h, 0.0001) > a) w = h * a;
                else h = w / a;
            }
            double x = q.X < _anchor.X ? _anchor.X - w : _anchor.X;
            double y = q.Y < _anchor.Y ? _anchor.Y - h : _anchor.Y;
            _sel = new Rect(x, y, Math.Max(w, 1), Math.Max(h, 1));
            Changed();
        }

        /// <summary>One edge follows the pointer; with a locked ratio the other dimension grows around the centre.</summary>
        private void DragEdge(Point q)
        {
            var s = _selAtStart;
            double left = s.Left, top = s.Top, right = s.Right, bottom = s.Bottom;
            switch (_drag)
            {
                case Hit.W: left = Math.Min(q.X, right - MinSize); break;
                case Hit.E: right = Math.Max(q.X, left + MinSize); break;
                case Hit.N: top = Math.Min(q.Y, bottom - MinSize); break;
                case Hit.S: bottom = Math.Max(q.Y, top + MinSize); break;
            }

            if (_aspect is double a)
            {
                if (_drag is Hit.W or Hit.E)
                {
                    double cy = s.Y + s.Height / 2, maxH = 2 * Math.Min(cy, ImageHeight - cy);
                    double w = right - left, h = w / a;
                    if (h > maxH)
                    {
                        h = maxH;
                        w = h * a;
                        if (_drag == Hit.W) left = right - w; else right = left + w;
                    }
                    top = cy - h / 2;
                    bottom = cy + h / 2;
                }
                else
                {
                    double cx = s.X + s.Width / 2, maxW = 2 * Math.Min(cx, ImageWidth - cx);
                    double h = bottom - top, w = h * a;
                    if (w > maxW)
                    {
                        w = maxW;
                        h = w / a;
                        if (_drag == Hit.N) top = bottom - h; else bottom = top + h;
                    }
                    left = cx - w / 2;
                    right = cx + w / 2;
                }
            }

            _sel = new Rect(new Point(left, top), new Point(right, bottom));
            Changed();
        }

        // ---- Helpers ----------------------------------------------------------------------------------------

        private static T Frozen<T>(T f) where T : Freezable
        {
            f.Freeze();
            return f;
        }

        /// <summary>Grey checkerboard behind transparent images.</summary>
        private static Brush CreateChecker()
        {
            var light = new SolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x3A));
            var dark = new SolidColorBrush(Color.FromRgb(0x2C, 0x2C, 0x2C));
            var group = new DrawingGroup();
            group.Children.Add(new GeometryDrawing(light, null, new RectangleGeometry(new Rect(0, 0, 16, 16))));
            group.Children.Add(new GeometryDrawing(dark, null, new RectangleGeometry(new Rect(0, 0, 8, 8))));
            group.Children.Add(new GeometryDrawing(dark, null, new RectangleGeometry(new Rect(8, 8, 8, 8))));
            var brush = new DrawingBrush(group) { TileMode = TileMode.Tile, Viewport = new Rect(0, 0, 16, 16), ViewportUnits = BrushMappingMode.Absolute };
            brush.Freeze();
            return brush;
        }
    }
}
