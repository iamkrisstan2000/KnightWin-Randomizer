using System;
using System.Windows;
using System.Windows.Media;

namespace KnightWin.Controls
{
    /// <summary>
    /// One uninterrupted title-bar + rail surface, with a concave rounded inside
    /// corner. Drawing both arms as ONE geometry avoids overlapping semi-transparent
    /// borders (which would darken the join or expose a one-pixel seam).
    /// </summary>
    public sealed class ChromeSurface : FrameworkElement
    {
        public static readonly DependencyProperty NavWidthProperty = DependencyProperty.Register(
            nameof(NavWidth), typeof(double), typeof(ChromeSurface),
            new FrameworkPropertyMetadata(220d, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty TitleHeightProperty = DependencyProperty.Register(
            nameof(TitleHeight), typeof(double), typeof(ChromeSurface),
            new FrameworkPropertyMetadata(44d, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty InsideRadiusProperty = DependencyProperty.Register(
            nameof(InsideRadius), typeof(double), typeof(ChromeSurface),
            new FrameworkPropertyMetadata(12d, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty ChromeBrushProperty = DependencyProperty.Register(
            nameof(ChromeBrush), typeof(Brush), typeof(ChromeSurface),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty AccentBrushProperty = DependencyProperty.Register(
            nameof(AccentBrush), typeof(Brush), typeof(ChromeSurface),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

        public double NavWidth { get => (double)GetValue(NavWidthProperty); set => SetValue(NavWidthProperty, value); }
        public double TitleHeight { get => (double)GetValue(TitleHeightProperty); set => SetValue(TitleHeightProperty, value); }
        public double InsideRadius { get => (double)GetValue(InsideRadiusProperty); set => SetValue(InsideRadiusProperty, value); }
        public Brush? ChromeBrush { get => (Brush?)GetValue(ChromeBrushProperty); set => SetValue(ChromeBrushProperty, value); }
        public Brush? AccentBrush { get => (Brush?)GetValue(AccentBrushProperty); set => SetValue(AccentBrushProperty, value); }

        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);
            double w = RenderSize.Width, h = RenderSize.Height;
            if (w <= 0 || h <= 0) return;
            double nav = Math.Clamp(NavWidth, 0, w);
            double title = Math.Clamp(TitleHeight, 0, h);
            double radius = Math.Clamp(InsideRadius, 0, Math.Min(w - nav, h - title));
            const double k = 0.5522847498; // cubic approximation of a circular quarter

            var geometry = new StreamGeometry();
            using (var path = geometry.Open())
            {
                path.BeginFigure(new Point(0, 0), isFilled: true, isClosed: true);
                path.LineTo(new Point(w, 0), isStroked: true, isSmoothJoin: false);
                path.LineTo(new Point(w, title), true, false);
                path.LineTo(new Point(nav + radius, title), true, false);
                if (radius > 0)
                    path.BezierTo(new Point(nav + radius * (1 - k), title),
                        new Point(nav, title + radius * (1 - k)),
                        new Point(nav, title + radius), true, false);
                path.LineTo(new Point(nav, h), true, false);
                path.LineTo(new Point(0, h), true, false);
            }
            geometry.Freeze();
            dc.DrawGeometry(ChromeBrush, null, geometry);
            dc.DrawGeometry(AccentBrush, null, geometry);
        }
    }
}
