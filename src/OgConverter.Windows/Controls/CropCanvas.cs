using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace OgConverter.Controls
{
    /// <summary>
    /// Shows an image fitted to the control with a crop rectangle on top: drag inside to move it, drag a handle
    /// to resize it, drag outside it to draw a new one. The selection is kept in image pixels.
    /// Ctrl+scroll zooms in at the pointer; scroll, Shift+scroll, the middle button or Space+drag pan.
    /// </summary>
    public class CropCanvas : FrameworkElement
    {
        private const double Pad = 14;
        private const double HandleHit = 12;
        private const double MinSize = 8;
        private const double MaxPixelScale = 8;
        private const double ScrollStep = 80;

        private enum Hit { None, Move, N, S, E, W, NE, NW, SE, SW, Draw }

        private BitmapSource? _source;
        private Rect _sel;
        private double? _aspect;

        private Hit _drag = Hit.None;
        private Point _dragStart;
        private Rect _selAtStart;
        private Point _anchor;
        private Point _lastPointer;

        private double _zoom = 1;
        private Point _view;
        private bool _panning;
        private Point _panLast;
        private readonly DispatcherTimer _autoScroll = new() { Interval = TimeSpan.FromMilliseconds(30) };

        private static readonly Brush Dim = Frozen(new SolidColorBrush(Color.FromArgb(0x99, 0, 0, 0)));
        private static readonly Pen Border = Frozen(new Pen(Brushes.White, 1.5));
        private static readonly Pen Thirds = Frozen(new Pen(new SolidColorBrush(Color.FromArgb(0x70, 255, 255, 255)), 1));
        private static readonly Pen HandleOutline = Frozen(new Pen(new SolidColorBrush(Color.FromArgb(0x60, 0, 0, 0)), 1));
        private static readonly Brush Checker = CreateChecker();

        public event EventHandler? SelectionChanged;

        /// <summary>The zoom or the visible part of the picture changed.</summary>
        public event EventHandler? ViewChanged;

        public CropCanvas()
        {
            Focusable = true;
            ClipToBounds = true;
            RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.HighQuality);
            _autoScroll.Tick += OnAutoScroll;
        }

        public BitmapSource? Source
        {
            get => _source;
            set
            {
                _source = value;
                _aspect = null;
                _zoom = 1;
                _view = new Point(ImageWidth / 2.0, ImageHeight / 2.0);
                Reset();
                ViewChanged?.Invoke(this, EventArgs.Empty);
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

        // ---- Zoom -------------------------------------------------------------------------------------------

        /// <summary>Screen pixels per image pixel (1 = 100%).</summary>
        public double PixelScale => Scale;

        public bool CanZoomIn => _source != null && _zoom < MaxZoom - 0.001;
        public bool CanZoomOut => _zoom > 1.001;

        /// <summary>
        /// Zooms by <paramref name="factor"/>, keeping the picture's point under <paramref name="at"/> (control
        /// coordinates; the middle of the view when null) where it is. Never smaller than the whole picture.
        /// </summary>
        public void ZoomBy(double factor, Point? at = null)
        {
            if (_source == null) return;
            var p = at ?? new Point(ActualWidth / 2, ActualHeight / 2);
            var img = ImageRect;
            double s = Scale;
            var q = new Point((p.X - img.X) / s, (p.Y - img.Y) / s);
            _zoom = Math.Clamp(_zoom * factor, 1, MaxZoom);
            double s2 = Scale;
            SetView(new Point(q.X - (p.X - ActualWidth / 2) / s2, q.Y - (p.Y - ActualHeight / 2) / s2));
        }

        /// <summary>Shows the whole picture.</summary>
        public void ZoomToFit()
        {
            _zoom = 1;
            SetView(new Point(ImageWidth / 2.0, ImageHeight / 2.0));
        }

        /// <summary>Fills the view's width with the picture, at the top of the selection: for long pages and screenshots.</summary>
        public void ZoomToWidth()
        {
            if (_source == null) return;
            _zoom = Math.Clamp((ActualWidth - 2 * Pad) / ImageWidth / FitScale, 1, MaxZoom);
            SetView(new Point(ImageWidth / 2.0, _sel.Top + (ActualHeight / 2 - Pad) / Scale));
        }

        /// <summary>Moves the visible part of the picture by screen pixels.</summary>
        public void PanBy(double dx, double dy)
        {
            if (_source == null) return;
            double s = Scale;
            var v = ClampView(_view, s);
            SetView(new Point(v.X + dx / s, v.Y + dy / s));
        }

        private void SetView(Point view)
        {
            _view = ClampView(view, Scale);
            InvalidateVisual();
            ViewChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>A picture smaller than the view stays centred; a larger one can't be scrolled past its edges.</summary>
        private Point ClampView(Point v, double s)
        {
            double halfW = Math.Max(0, ActualWidth / 2 - Pad), halfH = Math.Max(0, ActualHeight / 2 - Pad);
            double x = ImageWidth * s <= 2 * halfW ? ImageWidth / 2.0 : Math.Clamp(v.X, halfW / s, ImageWidth - halfW / s);
            double y = ImageHeight * s <= 2 * halfH ? ImageHeight / 2.0 : Math.Clamp(v.Y, halfH / s, ImageHeight - halfH / s);
            return new Point(x, y);
        }

        protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
        {
            base.OnRenderSizeChanged(sizeInfo);
            ViewChanged?.Invoke(this, EventArgs.Empty);
        }

        // ---- Layout -----------------------------------------------------------------------------------------

        /// <summary>The scale that shows the whole picture (at most 8×).</summary>
        private double FitScale => _source == null ? 1
            : Math.Max(0.0001, Math.Min(Math.Min((ActualWidth - 2 * Pad) / ImageWidth, (ActualHeight - 2 * Pad) / ImageHeight), MaxPixelScale));

        private double MaxZoom => Math.Max(1, MaxPixelScale / FitScale);

        private double Scale => FitScale * _zoom;

        private Rect ImageRect
        {
            get
            {
                double s = Scale;
                var v = ClampView(_view, s);
                return new Rect(ActualWidth / 2 - v.X * s, ActualHeight / 2 - v.Y * s, ImageWidth * s, ImageHeight * s);
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

        protected override void OnMouseWheel(MouseWheelEventArgs e)
        {
            base.OnMouseWheel(e);
            if (_source == null) return;
            double notches = e.Delta / 120.0;
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) ZoomBy(Math.Pow(1.25, notches), e.GetPosition(this));
            else if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) PanBy(-notches * ScrollStep, 0);
            else PanBy(0, -notches * ScrollStep);
            e.Handled = true;
        }

        protected override void OnMouseDown(MouseButtonEventArgs e)
        {
            base.OnMouseDown(e);
            if (_source == null || e.ChangedButton != MouseButton.Middle) return;
            StartPan(e.GetPosition(this));
            e.Handled = true;
        }

        protected override void OnMouseUp(MouseButtonEventArgs e)
        {
            base.OnMouseUp(e);
            if (!_panning || e.ChangedButton is not (MouseButton.Middle or MouseButton.Left)) return;
            _panning = false;
            ReleaseMouseCapture();
            Cursor = CursorFor(HitTest(e.GetPosition(this)));
            e.Handled = true;
        }

        private void StartPan(Point p)
        {
            Focus();
            _panning = true;
            _panLast = p;
            Cursor = Cursors.SizeAll;
            CaptureMouse();
        }

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e);
            if (_source == null) return;
            Focus();

            var p = e.GetPosition(this);
            if (Keyboard.IsKeyDown(Key.Space))
            {
                StartPan(p);
                e.Handled = true;
                return;
            }

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
            _lastPointer = p;
            _autoScroll.Start();
            e.Handled = true;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_source == null) return;

            var p = e.GetPosition(this);
            if (_panning)
            {
                PanBy(_panLast.X - p.X, _panLast.Y - p.Y);
                _panLast = p;
                return;
            }
            if (_drag == Hit.None)
            {
                Cursor = CursorFor(HitTest(p));
                return;
            }

            _lastPointer = p;
            ApplyDrag(p);
        }

        /// <summary>While a handle is dragged past the edge of the view, the picture scrolls toward it.</summary>
        private void OnAutoScroll(object? sender, EventArgs e)
        {
            if (_drag == Hit.None)
            {
                _autoScroll.Stop();
                return;
            }
            double dx = Overshoot(_lastPointer.X, ActualWidth), dy = Overshoot(_lastPointer.Y, ActualHeight);
            if (dx == 0 && dy == 0) return;
            var before = ImageRect;
            PanBy(Math.Clamp(dx, -60, 60) * 0.5, Math.Clamp(dy, -60, 60) * 0.5);
            if (ImageRect != before) ApplyDrag(_lastPointer);

            static double Overshoot(double v, double size) => v < Pad ? v - Pad : v > size - Pad ? v - (size - Pad) : 0;
        }

        private void ApplyDrag(Point p)
        {
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
            _autoScroll.Stop();
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
