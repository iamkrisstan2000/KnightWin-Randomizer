using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using KnightWin.Theme;

namespace KnightWin.Controls
{
    /// <summary>
    /// A compact, GPU-cheap "wheel of names". Renders N wedges with a pointer and a
    /// centre hub; <see cref="Spin"/> runs a decelerating rotation. No timers, no
    /// composition layer rebuild — one DrawingContext pass per frame managed by WPF.
    /// </summary>
    public class WheelControl : FrameworkElement
    {
        public static readonly DependencyProperty RotationProperty =
            DependencyProperty.Register(nameof(Rotation), typeof(double), typeof(WheelControl),
                new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty WedgesProperty =
            DependencyProperty.Register(nameof(Wedges), typeof(int), typeof(WheelControl),
                new FrameworkPropertyMetadata(12, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty SpinningProperty =
            DependencyProperty.Register(nameof(Spinning), typeof(bool), typeof(WheelControl),
                new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty AccentProperty =
            DependencyProperty.Register(nameof(Accent), typeof(Color), typeof(WheelControl),
                new FrameworkPropertyMetadata(Colors.DeepSkyBlue, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty HubBrushProperty =
            DependencyProperty.Register(nameof(HubBrush), typeof(Brush), typeof(WheelControl),
                new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender));

        public double Rotation
        {
            get => (double)GetValue(RotationProperty);
            set => SetValue(RotationProperty, value);
        }

        public int Wedges
        {
            get => (int)GetValue(WedgesProperty);
            set => SetValue(WedgesProperty, value);
        }

        public bool Spinning
        {
            get => (bool)GetValue(SpinningProperty);
            set => SetValue(SpinningProperty, value);
        }

        public Color Accent
        {
            get => (Color)GetValue(AccentProperty);
            set => SetValue(AccentProperty, value);
        }

        public Brush HubBrush
        {
            get => (Brush)GetValue(HubBrushProperty);
            set => SetValue(HubBrushProperty, value);
        }

        public event EventHandler? SpinCompleted;

        public WheelControl()
        {
            SnapsToDevicePixels = true;
            UseLayoutRounding = true;
            Width = 56;
            Height = 56;
        }

        // One frozen vector drawing is reused through every animation frame. Previously
        // each 30px spin frame allocated twelve geometries, twelve brushes and three pens.
        // Rebuild only when size, wedge count, or accent actually changes.
        private DrawingGroup? _wheelArt;
        private Geometry? _pointerArt;
        private Brush? _pointerBrush;
        private Pen? _ringPen;
        private double _artWidth = -1, _artHeight = -1;
        private int _artCount;
        private Color _artAccent;
        private static readonly Pen RimPen = CreateRimPen();

        private static Pen CreateRimPen()
        {
            var p = new Pen(new SolidColorBrush(Color.FromArgb(80, 255, 255, 255)), 1);
            p.Freeze();
            return p;
        }

        private void EnsureArt(Point center, double r, int n)
        {
            if (_wheelArt != null && _artWidth == ActualWidth && _artHeight == ActualHeight &&
                _artCount == n && _artAccent == Accent) return;

            var art = new DrawingGroup();
            using (var ctx = art.Open())
            {
                double step = 360.0 / n;
                for (int i = 0; i < n; i++)
                {
                    double a0 = i * step * Math.PI / 180.0;
                    double a1 = (i + 1) * step * Math.PI / 180.0;
                    var p0 = new Point(center.X + r * Math.Cos(a0), center.Y + r * Math.Sin(a0));
                    var p1 = new Point(center.X + r * Math.Cos(a1), center.Y + r * Math.Sin(a1));
                    var geo = new StreamGeometry();
                    using (var g = geo.Open())
                    {
                        g.BeginFigure(center, true, true);
                        g.LineTo(p0, true, false);
                        g.ArcTo(p1, new Size(r, r), 0, step > 180, SweepDirection.Clockwise, true, false);
                    }
                    geo.Freeze();

                    bool even = i % 2 == 0;
                    Color c = i % (n / 2) == 0 ? Accent : Mix(Accent, even ? 0.62 : 0.42);
                    var brush = new SolidColorBrush(c) { Opacity = even ? 0.95 : 0.72 };
                    brush.Freeze();
                    ctx.DrawGeometry(brush, null, geo);
                }
            }
            art.Freeze();
            var pointer = new StreamGeometry();
            using (var g = pointer.Open())
            {
                g.BeginFigure(new Point(center.X - 5, center.Y - r - 1), true, true);
                g.LineTo(new Point(center.X + 5, center.Y - r - 1), true, false);
                g.LineTo(new Point(center.X, center.Y - r + 7), true, false);
            }
            pointer.Freeze();
            var color = new SolidColorBrush(Accent);
            color.Freeze();
            var ringBrush = new SolidColorBrush(Color.FromArgb(90, Accent.R, Accent.G, Accent.B));
            ringBrush.Freeze();
            var ring = new Pen(ringBrush, 1.4);
            ring.Freeze();

            _wheelArt = art;
            _pointerArt = pointer;
            _pointerBrush = color;
            _ringPen = ring;
            _artWidth = ActualWidth;
            _artHeight = ActualHeight;
            _artCount = n;
            _artAccent = Accent;
        }

        protected override void OnRender(DrawingContext dc)
        {
            double size = Math.Min(ActualWidth, ActualHeight);
            if (size <= 4) return;
            var center = new Point(ActualWidth / 2, ActualHeight / 2);
            double r = size / 2 - 2;
            EnsureArt(center, r, Math.Clamp(Wedges, 4, 24));

            dc.PushTransform(new RotateTransform(Rotation, center.X, center.Y));
            dc.DrawDrawing(_wheelArt);
            dc.Pop();

            // The rim, hub and pointer stay fixed while the cached wedges rotate.
            dc.DrawEllipse(null, RimPen, center, r, r);
            double hub = r * 0.42;
            dc.DrawEllipse(HubBrush, null, center, hub, hub);
            dc.DrawEllipse(null, _ringPen, center, hub, hub);
            dc.DrawGeometry(_pointerBrush, null, _pointerArt);
        }

        private static Color Mix(Color c, double factor)
        {
            // blend towards a lighter tone of the accent for wedge variety
            return Color.FromRgb(
                (byte)Math.Clamp(c.R + (255 - c.R) * (1 - factor) * 0.55, 0, 255),
                (byte)Math.Clamp(c.G + (255 - c.G) * (1 - factor) * 0.55, 0, 255),
                (byte)Math.Clamp(c.B + (255 - c.B) * (1 - factor) * 0.55, 0, 255));
        }

        /// <summary>
        /// Spins with an ease-out curve and lands on a random wedge boundary.
        /// The rotation is unwrapped before animating so the motion never takes a shortcut
        /// backwards, which is what makes repeated rolls look smooth instead of jumpy.
        /// </summary>
        public void Spin(TimeSpan duration, int turns = 4)
        {
            Spinning = true;
            double from = Normalise(Rotation);
            double jitter = Random.Shared.NextDouble() * 360.0;
            double to = from + 360.0 * Math.Max(1, turns) + jitter;

            var anim = new DoubleAnimation(from, to, new Duration(duration))
            {
                EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseOut },
                FillBehavior = FillBehavior.HoldEnd
            };
            anim.Completed += (_, _) =>
            {
                BeginAnimation(RotationProperty, null);
                Rotation = to % 360.0;
                Spinning = false;
                SpinCompleted?.Invoke(this, EventArgs.Empty);
            };
            BeginAnimation(RotationProperty, anim);
        }

        /// <summary>
        /// Continuous, steady rotation used as a busy indicator while a batch runs.
        /// Implemented as an infinite linear animation, so the cost stays on the render thread
        /// and the app does no per-frame work of its own.
        /// </summary>
        public void StartBusySpin(double secondsPerTurn = 1.1)
        {
            _busy = true;
            Spinning = true;
            double from = Normalise(Rotation);
            Rotation = from;
            var anim = new DoubleAnimation(from, from + 360.0, new Duration(TimeSpan.FromSeconds(secondsPerTurn)))
            {
                RepeatBehavior = RepeatBehavior.Forever
            };
            BeginAnimation(RotationProperty, anim, HandoffBehavior.SnapshotAndReplace);
        }

        /// <summary>
        /// Stops the busy spin by easing the current angle a short way to a stop, then handing
        /// back to whatever comes next. Without the glide the wheel would freeze mid-frame.
        /// </summary>
        public void StopBusySpin(TimeSpan glide, Action? onStopped = null)
        {
            if (!_busy) { onStopped?.Invoke(); return; }

            // Freeze the animated value at this instant so the glide starts from what is on screen.
            double current = Normalise(Rotation);
            BeginAnimation(RotationProperty, null);
            Rotation = current;

            double to = current + 42.0;
            var anim = new DoubleAnimation(current, to, new Duration(glide))
            {
                EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseOut },
                FillBehavior = FillBehavior.HoldEnd
            };
            anim.Completed += (_, _) =>
            {
                BeginAnimation(RotationProperty, null);
                Rotation = to % 360.0;
                _busy = false;
                Spinning = false;
                onStopped?.Invoke();
            };
            BeginAnimation(RotationProperty, anim, HandoffBehavior.SnapshotAndReplace);
        }

        public bool IsBusy => _busy;

        private bool _busy;

        private static double Normalise(double degrees)
        {
            double d = degrees % 360.0;
            return d < 0 ? d + 360.0 : d;
        }
    }
}
