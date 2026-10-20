using System;
using System.Collections.Generic;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using OgConverter.Models;
using OgConverter.Services;
using OgConverter.Wheel;

namespace OgConverter.Controls
{
    /// <summary>
    /// Hosts a wheel: holds the options and hub text, runs the hover/open/switch animations, and hands each frame
    /// to the active template's <see cref="IWheelRenderer"/>. The Avalonia port of the Windows control.
    /// </summary>
    public class RadialWheelCanvas : Control
    {
        public const double BaseOuterRadius = 188;
        public const double BaseSize = 460;
        public const double MinScale = 0.4;
        public const double MaxScale = 1.4;

        /// <summary>Skips animations everywhere (Settings › Reduce motion).</summary>
        public static bool ReduceMotion { get; set; }

        private int _hoveredIndex = -1;
        private string _centerTitle = "Drag to format";
        private string _centerSubtitle = string.Empty;
        private string _centerGlyph = "";
        private string _centerBadge = string.Empty;
        private WheelMode _currentMode = WheelMode.Convert;
        private double _wheelScale = 1.0;

        public RadialWheelCanvas()
        {
            _accentShown = Palette.AccentFor(_currentMode);
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            TemplateManager.Instance.CurrentChanged += OnGlobalTemplateChanged;
            _accentShown = Palette.AccentFor(CurrentMode);
            if (_animating) RequestFrame();
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnDetachedFromVisualTree(e);
            TemplateManager.Instance.CurrentChanged -= OnGlobalTemplateChanged;
            StopAnimating();
        }

        /// <summary>Option under the pointer or the drag, -1 for none. Plays the tick and animates the highlight.</summary>
        public int HoveredIndex
        {
            get => _hoveredIndex;
            set
            {
                if (value == _hoveredIndex) return;
                _hoveredIndex = value;
                if (value >= 0 && PlaySounds) SoundService.Instance.Play(UiSound.Tick, ActiveTemplate);
                StartAnimating();
                InvalidateVisual();
            }
        }

        /// <summary>Hub instruction line (idle) or headline when no file is loaded.</summary>
        public string CenterTitle
        {
            get => _centerTitle;
            set { _centerTitle = value ?? string.Empty; InvalidateVisual(); }
        }

        /// <summary>Hub file name (idle).</summary>
        public string CenterSubtitle
        {
            get => _centerSubtitle;
            set { _centerSubtitle = value ?? string.Empty; InvalidateVisual(); }
        }

        /// <summary>Segoe Fluent Icons code point shown in the hub when no file badge is set (mapped by <see cref="Icons.Icons"/>).</summary>
        public string CenterGlyph
        {
            get => _centerGlyph;
            set { _centerGlyph = value ?? string.Empty; InvalidateVisual(); }
        }

        /// <summary>Short file-type label shown as a pill in the hub (e.g. "PDF"). Empty hides it.</summary>
        public string CenterBadge
        {
            get => _centerBadge;
            set { _centerBadge = value ?? string.Empty; InvalidateVisual(); }
        }

        /// <summary>The wheel shown. Changing it plays the switch sound and the template's switch animation.</summary>
        public WheelMode CurrentMode
        {
            get => _currentMode;
            set
            {
                if (value == _currentMode) return;
                _currentMode = value;
                if (PlaySounds) SoundService.Instance.Play(UiSound.Switch, ActiveTemplate);
                _accentFrom = _accentShown;
                _accentT = 0;
                BeginEntrance(switching: true);
                InvalidateVisual();
            }
        }

        public double WheelScale
        {
            get => _wheelScale;
            set { _wheelScale = value; InvalidateVisual(); }
        }

        private WheelTemplate? _templateOverride;

        /// <summary>Draw with this template instead of the active one (gallery previews).</summary>
        public WheelTemplate? TemplateOverride
        {
            get => _templateOverride;
            set
            {
                _templateOverride = value;
                _accentShown = Palette.AccentFor(CurrentMode);
                InvalidateVisual();
            }
        }

        /// <summary>Hover and switch sounds. Off for previews.</summary>
        public bool PlaySounds { get; set; } = true;

        public WheelTemplate ActiveTemplate => _templateOverride ?? TemplateManager.Instance.Current;
        private WheelPalette Palette => WheelPalette.For(ActiveTemplate);

        private double ClampedScale => Math.Clamp(WheelScale, MinScale - 0.05, 1.5);
        public double OuterRadius => BaseOuterRadius * ClampedScale;
        public double InnerRadius => OuterRadius * Math.Clamp(ActiveTemplate.Shape?.InnerRatio ?? 0.404, 0.25, 0.6);

        private List<WheelItem> _items = new();
        public List<WheelItem> Items
        {
            get => _items;
            set
            {
                _items = value ?? new List<WheelItem>();
                if (_hover.Length != _items.Count) _hover = new double[_items.Count];
                else Array.Clear(_hover);
                InvalidateVisual();
            }
        }

        private void OnGlobalTemplateChanged()
        {
            if (_templateOverride != null) return;
            _accentShown = Palette.AccentFor(CurrentMode);
            InvalidateVisual();
        }

        /// <summary>Shows a file in the hub: type badge, file name and an instruction line.</summary>
        public void SetFile(string path, string instruction)
        {
            bool isFolder = System.IO.Directory.Exists(path);
            string ext = System.IO.Path.GetExtension(path).TrimStart('.').ToUpperInvariant();
            CenterBadge = isFolder ? "FOLDER" : (ext.Length is > 0 and <= 6 ? ext : "FILE");
            CenterSubtitle = System.IO.Path.GetFileName(path.TrimEnd('\\', '/'));
            CenterTitle = instruction;
            CenterGlyph = FormatRegistry.GetCategoryGlyph(path);
        }

        /// <summary>Clears the file and shows a glyph + headline + hint instead.</summary>
        public void SetMessage(string glyph, string title, string hint)
        {
            CenterBadge = string.Empty;
            CenterGlyph = glyph;
            CenterTitle = title;
            CenterSubtitle = hint;
        }

        /// <summary>Accent of <paramref name="mode"/> in the active template.</summary>
        public static Color AccentFor(WheelMode mode) => WheelPalette.For(TemplateManager.Instance.Current).AccentFor(mode);

        /// <summary>Index of the option at <paramref name="position"/> (in this control's coordinates), or -1.</summary>
        public int HitTestSlice(Point position)
        {
            if (_items.Count == 0) return -1;

            var frame = BuildFrame();
            try
            {
                int? custom = TemplateManager.Instance.RendererFor(ActiveTemplate).HitTest(position, frame);
                if (custom.HasValue) return custom.Value < _items.Count ? custom.Value : -1;
            }
            catch (Exception ex)
            {
                ReportRendererError(ex);
            }

            double s = ClampedScale;
            double dx = position.X - Bounds.Width / 2.0;
            double dy = position.Y - Bounds.Height / 2.0;
            double dist = Math.Sqrt(dx * dx + dy * dy);
            if (dist < InnerRadius - 10 * s || dist > OuterRadius + 30 * s) return -1;

            // Atan2: 0 is right, 90 is down; rotate so the top is 0
            double deg = Math.Atan2(dy, dx) * 180.0 / Math.PI;
            if (deg < 0) deg += 360.0;
            deg = (deg + 90.0) % 360.0;

            int idx = (int)(deg / (360.0 / _items.Count));
            return idx >= 0 && idx < _items.Count ? idx : -1;
        }

        // ------------------------------------------------------------------ animation

        private enum Entrance { None, Pop, Spring, Spin, Bloom, Fade, SwitchStagger, SwitchSpin, SwitchFade }

        private double[] _hover = Array.Empty<double>();
        private double _entranceT = 1;
        private Entrance _entrance = Entrance.None;
        private Color _accentShown;
        private Color _accentFrom;
        private double _accentT = 1;
        private bool _animating;
        private bool _frameRequested;
        private readonly Stopwatch _clock = new();
        private TimeSpan _lastFrame;

        /// <summary>Plays the template's open animation (call right after showing the wheel).</summary>
        public void PlayIntro() => BeginEntrance(switching: false);

        private void BeginEntrance(bool switching)
        {
            var m = ActiveTemplate.Motion ?? new WheelMotion();
            string style = (switching ? m.Switch : m.Open)?.Trim().ToLowerInvariant() ?? string.Empty;
            _entrance = (switching, style) switch
            {
                (false, "pop") => Entrance.Pop,
                (false, "spring") => Entrance.Spring,
                (false, "spin") => Entrance.Spin,
                (false, "bloom") => Entrance.Bloom,
                (false, "fade") => Entrance.Fade,
                (true, "stagger") => Entrance.SwitchStagger,
                (true, "spin") => Entrance.SwitchSpin,
                (true, "fade") => Entrance.SwitchFade,
                _ => Entrance.None
            };
            _entranceT = _entrance == Entrance.None || ReduceMotion ? 1 : 0;
            if (!switching) _accentT = 1;
            StartAnimating();
        }

        private void StartAnimating()
        {
            if (ReduceMotion)
            {
                SnapAll();
                InvalidateVisual();
                return;
            }
            if (_animating) return;
            _animating = true;
            _clock.Restart();
            _lastFrame = TimeSpan.Zero;
            RequestFrame();
        }

        private void StopAnimating()
        {
            if (!_animating) return;
            _animating = false;
            _clock.Stop();
        }

        private void RequestFrame()
        {
            if (_frameRequested) return;
            var top = TopLevel.GetTopLevel(this);
            if (top == null) return; // not shown yet: OnAttachedToVisualTree picks the animation up
            _frameRequested = true;
            top.RequestAnimationFrame(OnFrame);
        }

        private void SnapAll()
        {
            int hovered = HoveredIndex;
            for (int i = 0; i < _hover.Length; i++) _hover[i] = i == hovered ? 1 : 0;
            _entranceT = 1;
            _accentT = 1;
        }

        private void OnFrame(TimeSpan _)
        {
            _frameRequested = false;
            if (!_animating) return;

            var now = _clock.Elapsed;
            double dt = Math.Clamp((now - _lastFrame).TotalSeconds, 0, 0.05); // a stalled frame shouldn't jump the animation
            _lastFrame = now;
            var motion = ActiveTemplate.Motion ?? new WheelMotion();
            bool active = false;

            // Hover: critically damped approach toward 1 on the hovered option and 0 elsewhere
            double k = 1 - Math.Exp(-dt / Math.Max(0.008, motion.HoverMs / 3000.0));
            int hovered = HoveredIndex;
            for (int i = 0; i < _hover.Length; i++)
            {
                double target = i == hovered ? 1 : 0;
                double v = _hover[i] + (target - _hover[i]) * k;
                if (Math.Abs(target - v) < 0.004) v = target;
                else active = true;
                _hover[i] = v;
            }

            if (_entranceT < 1)
            {
                double duration = Math.Max(0.06, motion.DurationMs / 1000.0) * (_entrance >= Entrance.SwitchStagger ? 0.8 : 1.0);
                _entranceT = Math.Min(1, _entranceT + dt / duration);
                active |= _entranceT < 1;
            }

            if (_accentT < 1)
            {
                _accentT = Math.Min(1, _accentT + dt / 0.18);
                active |= _accentT < 1;
            }

            InvalidateVisual();
            if (active) RequestFrame();
            else StopAnimating();
        }

        private static double OutCubic(double t) => 1 - Math.Pow(1 - t, 3);
        private static double OutQuint(double t) => 1 - Math.Pow(1 - t, 5);
        private static double OutBack(double t, double c1 = 1.70158) => 1 + (c1 + 1) * Math.Pow(t - 1, 3) + c1 * Math.Pow(t - 1, 2);
        private static double Spring(double t) => t >= 1 ? 1 : 1 - Math.Exp(-7 * t) * Math.Cos(9 * t);

        /// <summary>Per-option progress: options start one after another, clockwise from the top.</summary>
        private double Stagger(int index)
        {
            int n = _items.Count;
            double delay = n > 1 ? 0.4 * index / (n - 1) : 0;
            return Math.Clamp((_entranceT - delay) / 0.6, 0, 1);
        }

        private (double Scale, double Opacity, double Rotation) WheelEntrance()
        {
            double t = _entranceT;
            if (t >= 1) return (1, 1, 0);
            double fadeIn = OutCubic(Math.Min(1, t * 2));
            return _entrance switch
            {
                Entrance.Pop => (0.86 + 0.14 * OutBack(t), fadeIn, 0),
                Entrance.Spring => (0.55 + 0.45 * Spring(t), fadeIn, 0),
                Entrance.Spin => (0.9 + 0.1 * OutCubic(t), fadeIn, -70 * (1 - OutCubic(t))),
                Entrance.Bloom => (0.62 + 0.38 * OutQuint(t), fadeIn, 0),
                Entrance.Fade => (1, OutCubic(t), 0),
                Entrance.SwitchSpin => (1, 1, -36 * (1 - OutCubic(t))),
                _ => (1, 1, 0)
            };
        }

        private double ItemAlpha(int i)
        {
            if (_entranceT >= 1) return 1;
            return _entrance switch
            {
                Entrance.Pop or Entrance.Spring or Entrance.Spin or Entrance.Bloom or Entrance.SwitchStagger => OutCubic(Stagger(i)),
                Entrance.SwitchSpin or Entrance.SwitchFade => OutCubic(_entranceT),
                _ => 1
            };
        }

        private double ItemScale(int i)
        {
            if (_entranceT >= 1) return 1;
            double t = Stagger(i);
            return _entrance switch
            {
                Entrance.Pop => 0.7 + 0.3 * OutBack(t),
                Entrance.Spring => 0.5 + 0.5 * Spring(t),
                Entrance.Bloom => 0.8 + 0.2 * OutCubic(t),
                Entrance.SwitchStagger => 0.75 + 0.25 * OutBack(t),
                _ => 1
            };
        }

        // ------------------------------------------------------------------ rendering

        private readonly WheelFrame _frame = new();
        private bool _rendererFailed;

        private WheelFrame BuildFrame()
        {
            var template = ActiveTemplate;
            var palette = WheelPalette.For(template);
            double s = ClampedScale;
            var density = s >= 0.8 ? WheelDensity.Full : s >= 0.55 ? WheelDensity.Compact : WheelDensity.Micro;

            Color target = palette.AccentFor(CurrentMode);
            _accentShown = _accentT >= 1 ? target : ColorUtil.Lerp(_accentFrom, target, OutCubic(_accentT));

            var f = _frame;
            f.Template = template;
            f.Palette = palette;
            f.Center = new Point(Bounds.Width / 2.0, Bounds.Height / 2.0);
            f.Scale = s;
            f.OuterRadius = OuterRadius;
            f.InnerRadius = InnerRadius;
            f.Density = density;
            // Text keeps a floor so small wheels stay readable; micro wheels show far less text, a little larger
            f.TextScale = density == WheelDensity.Micro ? Math.Max(s, 0.45) / 0.45 : Math.Max(s, 0.85);
            f.Items = _items;
            f.HoveredIndex = HoveredIndex;
            if (_hover.Length != _items.Count) _hover = new double[_items.Count];
            f.Hover = _hover;
            f.Mode = CurrentMode;
            f.Accent = _accentShown;
            f.CenterTitle = CenterTitle;
            f.CenterSubtitle = CenterSubtitle;
            f.CenterGlyph = CenterGlyph;
            f.CenterBadge = CenterBadge;
            f.Rotation = WheelEntrance().Rotation;
            f.ItemAlphaFunc = _entranceT >= 1 ? null : ItemAlpha;
            f.ItemScaleFunc = _entranceT >= 1 ? null : ItemScale;
            return f;
        }

        public override void Render(DrawingContext dc)
        {
            base.Render(dc);
            if (Bounds.Width < 10 || Bounds.Height < 10) return;

            // Without an animation running, the hover state must match the hovered index (e.g. snapshots, reduced motion)
            if (!_animating) SnapAll();

            var f = BuildFrame();
            var (scale, opacity, _) = WheelEntrance();
            DrawingContext.PushedState? fade = opacity < 0.999 ? dc.PushOpacity(opacity) : null;
            DrawingContext.PushedState? grow = Math.Abs(scale - 1) > 0.001 ? dc.PushTransform(WheelPainter.ScaleAt(scale, f.Center)) : null;

            var renderer = TemplateManager.Instance.RendererFor(f.Template);
            try
            {
                renderer.Render(dc, f);
            }
            catch (Exception ex)
            {
                // A broken renderer must not take the wheel down: fall back to the built-in ring
                ReportRendererError(ex);
                var ring = new Wheel.Renderers.RingRenderer();
                if (renderer.Id != ring.Id) ring.Render(dc, f);
            }

            grow?.Dispose();
            fade?.Dispose();
        }

        private void ReportRendererError(Exception ex)
        {
            if (_rendererFailed) return;
            _rendererFailed = true;
            Console.Error.WriteLine($"[OG Converter] Wheel renderer \"{ActiveTemplate.Renderer}\" failed: {ex}");
        }
    }
}
