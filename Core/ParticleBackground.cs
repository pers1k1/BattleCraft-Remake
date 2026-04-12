using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace CustomLauncher.Core
{
    public class ParticleBackground : Canvas
    {
        private const int Count = 30;
        private const double ConnDist = 180;
        private const double MouseDist = 220;
        private const int FragCount = 20;

        private readonly List<Tri> _tris = new();
        private readonly List<Line> _lines = new();
        private readonly List<UIElement> _debris = new();
        private readonly List<DispatcherTimer> _timers = new();
        private Point _mouse = new(-999, -999);
        private bool _active;
        private bool _paused;
        private DateTime _last;
        private readonly Random _rng = new();
        private SolidColorBrush _lineBrush;
        private SolidColorBrush _mouseBrush;
        public bool IsAnimating { get; private set; }

        private class Tri
        {
            public Polygon Shape = null!;
            public double X, Y, VX, VY, BaseVX, BaseVY, Rot, RotV, BaseRotV, Size;
            public TranslateTransform Tr = null!;
            public RotateTransform Rt = null!;
            public ScaleTransform Sc = null!;
            public bool Dissolved;
            public bool Fading;
        }

        private struct Frag
        {
            public Ellipse Dot;
            public TranslateTransform Tr;
            public double WorldX, WorldY, TipDist;
        }

        public ParticleBackground()
        {
            ClipToBounds = true;
            Background = Brushes.Transparent;
            IsHitTestVisible = false;
            _lineBrush = new SolidColorBrush(Color.FromArgb(60, 233, 69, 96));
            _lineBrush.Freeze();
            _mouseBrush = new SolidColorBrush(Color.FromArgb(100, 233, 69, 96));
            _mouseBrush.Freeze();
            Loaded += (s, e) => { if (!_active) { Reinit(); _active = true; _last = DateTime.UtcNow; CompositionTarget.Rendering += Tick; } };
            SizeChanged += (s, e) => { if (_active && !_paused && !IsAnimating && e.NewSize.Width > 0) Reinit(); };
        }

        public void SetMouse(Point p) => _mouse = p;
        public void ClearMouse() => _mouse = new Point(-999, -999);

        public void Pause()
        {
            if (!_active || _paused) return;
            _paused = true;
            CompositionTarget.Rendering -= Tick;
        }

        public void Resume()
        {
            if (!_active || !_paused) return;
            _paused = false;
            _last = DateTime.UtcNow;
            CompositionTarget.Rendering += Tick;
        }

        private Color GetAccent()
        {
            try { return ((SolidColorBrush)FindResource("AccentBrush")).Color; }
            catch { return Color.FromRgb(233, 69, 96); }
        }

        private void KillTimers() { foreach (var t in _timers) t.Stop(); _timers.Clear(); }

        private void CancelAll()
        {
            KillTimers();
            foreach (var d in _debris) Children.Remove(d);
            _debris.Clear();
            foreach (var t in _tris)
            {
                t.Shape.BeginAnimation(OpacityProperty, null);
                t.Sc.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                t.Sc.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                t.Shape.OpacityMask = null;
                t.Shape.Opacity = 1; t.Sc.ScaleX = 1; t.Sc.ScaleY = 1;
                t.Shape.Visibility = Visibility.Visible; t.Dissolved = false; t.Fading = false;
            }
            foreach (var l in _lines) l.BeginAnimation(OpacityProperty, null);
            IsAnimating = false;
        }

        private void Reinit()
        {
            Children.Clear(); _tris.Clear(); _lines.Clear(); _debris.Clear();
            double w = ActualWidth > 10 ? ActualWidth : 1100;
            double h = ActualHeight > 10 ? ActualHeight : 650;
            Color accent = GetAccent();
            int maxLines = Count * (Count - 1) / 2 + Count;
            for (int i = 0; i < maxLines; i++)
            {
                var l = new Line { Stroke = _lineBrush, StrokeThickness = 0.8, Visibility = Visibility.Collapsed, IsHitTestVisible = false };
                _lines.Add(l); Children.Add(l);
            }
            for (int i = 0; i < Count; i++)
                AddTri(accent, _rng.NextDouble() * w, _rng.NextDouble() * h);
        }

        private void AddTri(Color accent, double x, double y)
        {
            double depth = _rng.NextDouble();
            double size = 15 + depth * 35;
            double speed = 0.15 + depth * 0.4;
            var tr = new TranslateTransform(x, y);
            var rt = new RotateTransform(_rng.NextDouble() * 360);
            var sc = new ScaleTransform(1, 1);
            var tg = new TransformGroup(); tg.Children.Add(sc); tg.Children.Add(rt); tg.Children.Add(tr);
            byte a = (byte)(50 + depth * 80);
            var fill = new SolidColorBrush(Color.FromArgb((byte)(a * 0.35), accent.R, accent.G, accent.B));
            var stroke = new SolidColorBrush(Color.FromArgb(a, accent.R, accent.G, accent.B));
            fill.Freeze(); stroke.Freeze();
            var poly = new Polygon
            {
                Points = new PointCollection { new Point(0, -size * 0.65), new Point(-size * 0.56, size * 0.35), new Point(size * 0.56, size * 0.35) },
                Fill = fill, Stroke = stroke, StrokeThickness = 1.0,
                RenderTransform = tg, RenderTransformOrigin = new Point(0, 0), IsHitTestVisible = false
            };
            double vx = (_rng.NextDouble() - 0.5) * speed;
            double vy = (_rng.NextDouble() - 0.5) * speed;
            double rv = (_rng.NextDouble() - 0.5) * 0.4;
            _tris.Add(new Tri
            {
                Shape = poly, Size = size, Tr = tr, Rt = rt, Sc = sc,
                X = x, Y = y, VX = vx, VY = vy, BaseVX = vx, BaseVY = vy,
                Rot = rt.Angle, RotV = rv, BaseRotV = rv
            });
            Children.Add(poly);
        }

        public void UpdateAccent(Color c)
        {
            _lineBrush = new SolidColorBrush(Color.FromArgb(60, c.R, c.G, c.B));
            _lineBrush.Freeze();
            _mouseBrush = new SolidColorBrush(Color.FromArgb(100, c.R, c.G, c.B));
            _mouseBrush.Freeze();
            foreach (var t in _tris)
            {
                double d = (t.Size - 15) / 35;
                byte a = (byte)(50 + d * 80);
                var f = new SolidColorBrush(Color.FromArgb((byte)(a * 0.35), c.R, c.G, c.B));
                var s = new SolidColorBrush(Color.FromArgb(a, c.R, c.G, c.B));
                f.Freeze(); s.Freeze();
                t.Shape.Fill = f; t.Shape.Stroke = s;
            }
        }

        private List<Frag> BuildFragments(double cx, double cy, double size, double rotDeg, Color accent)
        {
            var frags = new List<Frag>();
            var v0 = new Point(0, -size * 0.65);
            var v1 = new Point(-size * 0.56, size * 0.35);
            var v2 = new Point(size * 0.56, size * 0.35);
            double rad = rotDeg * Math.PI / 180;
            double cos = Math.Cos(rad), sin = Math.Sin(rad);
            double maxTipDist = 0;

            for (int i = 0; i < FragCount; i++)
            {
                double u = _rng.NextDouble(), vv = _rng.NextDouble();
                if (u + vv > 1) { u = 1 - u; vv = 1 - vv; }
                double lx = (1 - u - vv) * v0.X + u * v1.X + vv * v2.X;
                double ly = (1 - u - vv) * v0.Y + u * v1.Y + vv * v2.Y;
                double tipDist = Math.Sqrt((lx - v0.X) * (lx - v0.X) + (ly - v0.Y) * (ly - v0.Y));
                if (tipDist > maxTipDist) maxTipDist = tipDist;

                double rx = lx * cos - ly * sin;
                double ry = lx * sin + ly * cos;
                double wx = cx + rx;
                double wy = cy + ry;

                double sz = 1.2 + _rng.NextDouble() * 1.8;
                byte alpha = (byte)(90 + _rng.Next(100));
                var dot = new Ellipse
                {
                    Width = sz, Height = sz,
                    Fill = new SolidColorBrush(Color.FromArgb(alpha, accent.R, accent.G, accent.B)),
                    IsHitTestVisible = false
                };
                var dotTr = new TranslateTransform(wx - sz / 2, wy - sz / 2);
                dot.RenderTransform = dotTr;
                frags.Add(new Frag { Dot = dot, Tr = dotTr, WorldX = wx, WorldY = wy, TipDist = tipDist });
            }

            if (maxTipDist > 0)
                for (int i = 0; i < frags.Count; i++)
                {
                    var f = frags[i];
                    f.TipDist /= maxTipDist;
                    frags[i] = f;
                }
            return frags;
        }

        private void Dissolve(Tri t, int baseDelay, Action? onDone)
        {
            if (t.Dissolved || t.Fading) { onDone?.Invoke(); return; }
            t.Fading = true;
            Color accent = GetAccent();
            var frags = BuildFragments(t.X, t.Y, t.Size, t.Rot, accent);

            foreach (var f in frags) { Children.Add(f.Dot); _debris.Add(f.Dot); f.Dot.Opacity = 0; }

            int totalSpan = 900;

            var maskStop1 = new GradientStop(Colors.Transparent, -0.2);
            var maskStop2 = new GradientStop(Colors.Black, -0.05);
            var mask = new RadialGradientBrush(
                new GradientStopCollection { maskStop1, maskStop2 })
            { Center = new Point(0.5, 0.0), RadiusX = 0.9, RadiusY = 1.0, GradientOrigin = new Point(0.5, 0.0) };
            t.Shape.OpacityMask = mask;

            var sweepEase = new QuadraticEase { EasingMode = EasingMode.EaseIn };
            maskStop1.BeginAnimation(GradientStop.OffsetProperty,
                new DoubleAnimation(-0.2, 1.2, TimeSpan.FromMilliseconds(totalSpan))
                { BeginTime = TimeSpan.FromMilliseconds(baseDelay), EasingFunction = sweepEase });
            maskStop2.BeginAnimation(GradientStop.OffsetProperty,
                new DoubleAnimation(-0.05, 1.4, TimeSpan.FromMilliseconds(totalSpan))
                { BeginTime = TimeSpan.FromMilliseconds(baseDelay), EasingFunction = sweepEase });

            double tipRad = t.Rot * Math.PI / 180;
            double tipX = t.X + t.Size * 0.65 * Math.Sin(tipRad);
            double tipY = t.Y - t.Size * 0.65 * Math.Cos(tipRad);

            int finishedFrags = 0;
            for (int i = 0; i < frags.Count; i++)
            {
                var f = frags[i];
                double tipFactor = 1.0 - f.TipDist;
                int fragDelay = baseDelay + (int)(tipFactor * totalSpan * 0.7) + _rng.Next(50);
                int fragDur = 400 + _rng.Next(350);

                double dx = f.WorldX - tipX;
                double dy = f.WorldY - tipY;
                double dist = Math.Sqrt(dx * dx + dy * dy);
                if (dist < 1) dist = 1;
                double angle = Math.Atan2(dy, dx) + (_rng.NextDouble() - 0.5) * 0.4;
                double drift = dist + 15 + _rng.NextDouble() * 25;

                double sz = f.Dot.Width;
                f.Tr.X = tipX - sz / 2;
                f.Tr.Y = tipY - sz / 2;

                double endX = tipX - sz / 2 + Math.Cos(angle) * drift;
                double endY = tipY - sz / 2 + Math.Sin(angle) * drift;

                f.Dot.BeginAnimation(OpacityProperty,
                    new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(80))
                    { BeginTime = TimeSpan.FromMilliseconds(fragDelay) });

                var moveEase = new QuadraticEase { EasingMode = EasingMode.EaseOut };
                f.Tr.BeginAnimation(TranslateTransform.XProperty,
                    new DoubleAnimation(endX, TimeSpan.FromMilliseconds(fragDur))
                    { BeginTime = TimeSpan.FromMilliseconds(fragDelay), EasingFunction = moveEase });
                f.Tr.BeginAnimation(TranslateTransform.YProperty,
                    new DoubleAnimation(endY, TimeSpan.FromMilliseconds(fragDur))
                    { BeginTime = TimeSpan.FromMilliseconds(fragDelay), EasingFunction = moveEase });

                var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(fragDur * 0.5))
                { BeginTime = TimeSpan.FromMilliseconds(fragDelay + fragDur * 0.5) };

                Ellipse cd = f.Dot;
                fadeOut.Completed += (s, e) =>
                {
                    Children.Remove(cd); _debris.Remove(cd);
                    finishedFrags++;
                    if (finishedFrags >= frags.Count)
                    {
                        t.Shape.OpacityMask = null;
                        t.Shape.Visibility = Visibility.Collapsed;
                        t.Dissolved = true; t.Fading = false;
                        onDone?.Invoke();
                    }
                };
                f.Dot.BeginAnimation(OpacityProperty, fadeOut);
            }
        }

        private void Materialize(Tri t, double tx, double ty, int baseDelay, Action? onDone)
        {
            Color accent = GetAccent();
            t.X = tx; t.Y = ty; t.Tr.X = tx; t.Tr.Y = ty;
            t.VX = t.BaseVX; t.VY = t.BaseVY; t.RotV = t.BaseRotV;
            t.Dissolved = true; t.Fading = true;
            t.Shape.Opacity = 1; t.Sc.ScaleX = 1; t.Sc.ScaleY = 1;
            t.Shape.Visibility = Visibility.Visible;

            var maskStop1 = new GradientStop(Colors.Black, -0.2);
            var maskStop2 = new GradientStop(Colors.Transparent, -0.05);
            var mask = new RadialGradientBrush(
                new GradientStopCollection { maskStop1, maskStop2 })
            { Center = new Point(0.5, 0.0), RadiusX = 0.9, RadiusY = 1.0, GradientOrigin = new Point(0.5, 0.0) };
            t.Shape.OpacityMask = mask;

            var frags = BuildFragments(tx, ty, t.Size, t.Rot, accent);
            int totalSpan = 900;
            int finishedFrags = 0;

            var revealEase = new CubicEase { EasingMode = EasingMode.EaseOut };
            maskStop1.BeginAnimation(GradientStop.OffsetProperty,
                new DoubleAnimation(-0.2, 1.2, TimeSpan.FromMilliseconds(totalSpan))
                { BeginTime = TimeSpan.FromMilliseconds(baseDelay), EasingFunction = revealEase });
            var maskAnim = new DoubleAnimation(-0.05, 1.4, TimeSpan.FromMilliseconds(totalSpan))
            { BeginTime = TimeSpan.FromMilliseconds(baseDelay), EasingFunction = revealEase };
            maskAnim.Completed += (s, e) => { t.Shape.OpacityMask = null; };
            maskStop2.BeginAnimation(GradientStop.OffsetProperty, maskAnim);

            double tipRad = t.Rot * Math.PI / 180;
            double tipX = tx + t.Size * 0.65 * Math.Sin(tipRad);
            double tipY = ty - t.Size * 0.65 * Math.Cos(tipRad);

            for (int i = 0; i < frags.Count; i++)
            {
                var f = frags[i];
                int fragDelay = baseDelay + (int)(f.TipDist * totalSpan * 0.7) + _rng.Next(50);
                int fragDur = 400 + _rng.Next(300);

                double sz = f.Dot.Width;
                double dx = f.WorldX - tipX;
                double dy = f.WorldY - tipY;
                double toFragAngle = Math.Atan2(dy, dx) + (_rng.NextDouble() - 0.5) * 0.4;
                double scatter = 15 + _rng.NextDouble() * 20;
                double startX = tipX - sz / 2 + Math.Cos(toFragAngle) * scatter;
                double startY = tipY - sz / 2 + Math.Sin(toFragAngle) * scatter;
                double targetX = f.Tr.X;
                double targetY = f.Tr.Y;
                f.Tr.X = startX; f.Tr.Y = startY;

                f.Dot.Opacity = 0;
                Children.Add(f.Dot); _debris.Add(f.Dot);

                f.Dot.BeginAnimation(OpacityProperty,
                    new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(fragDur * 0.35))
                    { BeginTime = TimeSpan.FromMilliseconds(fragDelay) });

                var moveEase = new QuarticEase { EasingMode = EasingMode.EaseIn };
                f.Tr.BeginAnimation(TranslateTransform.XProperty,
                    new DoubleAnimation(targetX, TimeSpan.FromMilliseconds(fragDur))
                    { BeginTime = TimeSpan.FromMilliseconds(fragDelay), EasingFunction = moveEase });
                f.Tr.BeginAnimation(TranslateTransform.YProperty,
                    new DoubleAnimation(targetY, TimeSpan.FromMilliseconds(fragDur))
                    { BeginTime = TimeSpan.FromMilliseconds(fragDelay), EasingFunction = moveEase });

                var fadeEnd = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(120))
                { BeginTime = TimeSpan.FromMilliseconds(fragDelay + fragDur - 60) };

                Ellipse cd = f.Dot;
                fadeEnd.Completed += (s, e) =>
                {
                    Children.Remove(cd); _debris.Remove(cd);
                    finishedFrags++;
                    if (finishedFrags >= frags.Count)
                    {
                        t.Shape.OpacityMask = null;
                        t.Shape.Opacity = 1; t.Sc.ScaleX = 1; t.Sc.ScaleY = 1;
                        t.Dissolved = false; t.Fading = false;
                        onDone?.Invoke();
                    }
                };
                f.Dot.BeginAnimation(OpacityProperty, fadeEnd);
            }
        }

        private void Tick(object? sender, EventArgs e)
        {
            if (!_active || _paused || ActualWidth <= 10) return;
            var now = DateTime.UtcNow;
            double dt = Math.Min((now - _last).TotalSeconds, 0.05);
            _last = now;
            double w = ActualWidth, h = ActualHeight;
            double margin = 30;

            foreach (var t in _tris)
            {
                if (t.Fading || t.Dissolved) continue;
                t.VX += (t.BaseVX - t.VX) * 0.01;
                t.VY += (t.BaseVY - t.VY) * 0.01;
                t.RotV += (t.BaseRotV - t.RotV) * 0.01;
                t.X += t.VX * dt * 60; t.Y += t.VY * dt * 60; t.Rot += t.RotV * dt * 60;
                if (t.X < -margin) t.X = w + margin; if (t.X > w + margin) t.X = -margin;
                if (t.Y < -margin) t.Y = h + margin; if (t.Y > h + margin) t.Y = -margin;
                t.Tr.X = t.X; t.Tr.Y = t.Y; t.Rt.Angle = t.Rot;
            }

            int li = 0;
            for (int i = 0; i < _tris.Count && li < _lines.Count; i++)
            {
                if (_tris[i].Dissolved || _tris[i].Fading) continue;
                for (int j = i + 1; j < _tris.Count && li < _lines.Count; j++)
                {
                    if (_tris[j].Dissolved || _tris[j].Fading) continue;
                    double dx = _tris[i].X - _tris[j].X, dy = _tris[i].Y - _tris[j].Y;
                    double dist = dx * dx + dy * dy;
                    if (dist < ConnDist * ConnDist)
                    {
                        dist = Math.Sqrt(dist);
                        var l = _lines[li]; l.Visibility = Visibility.Visible;
                        l.X1 = _tris[i].X; l.Y1 = _tris[i].Y; l.X2 = _tris[j].X; l.Y2 = _tris[j].Y;
                        l.Stroke = _lineBrush; l.Opacity = 1.0 - dist / ConnDist; l.StrokeThickness = 0.8;
                        li++;
                    }
                }
            }

            if (_mouse.X > -900)
                for (int i = 0; i < _tris.Count && li < _lines.Count; i++)
                {
                    if (_tris[i].Dissolved || _tris[i].Fading) continue;
                    double dx = _tris[i].X - _mouse.X, dy = _tris[i].Y - _mouse.Y;
                    double dist = dx * dx + dy * dy;
                    if (dist < MouseDist * MouseDist)
                    {
                        dist = Math.Sqrt(dist);
                        var l = _lines[li]; l.Visibility = Visibility.Visible;
                        l.X1 = _tris[i].X; l.Y1 = _tris[i].Y; l.X2 = _mouse.X; l.Y2 = _mouse.Y;
                        l.Stroke = _mouseBrush; l.Opacity = 1.0 - dist / MouseDist; l.StrokeThickness = 1.2;
                        li++;
                    }
                }

            for (int i = li; i < _lines.Count; i++) _lines[i].Visibility = Visibility.Collapsed;
        }

        public void Stop()
        {
            CancelAll();
            _active = false; _paused = false;
            CompositionTarget.Rendering -= Tick;
            foreach (var t in _tris) { t.Shape.Visibility = Visibility.Collapsed; t.Dissolved = true; }
            Visibility = Visibility.Collapsed;
        }

        public void Start()
        {
            if (_active && !_paused) return;
            Visibility = Visibility.Visible; _active = true; _paused = false; _last = DateTime.UtcNow;
            foreach (var t in _tris) { t.Shape.Visibility = Visibility.Visible; t.Dissolved = false; t.Fading = false; t.Shape.Opacity = 1; t.Sc.ScaleX = 1; t.Sc.ScaleY = 1; }
            CompositionTarget.Rendering += Tick;
        }

        public void FadeIn(Action? onComplete = null)
        {
            CancelAll();
            Visibility = Visibility.Visible;
            if (!_active) { _active = true; _paused = false; _last = DateTime.UtcNow; CompositionTarget.Rendering += Tick; }
            else if (_paused) { _paused = false; _last = DateTime.UtcNow; CompositionTarget.Rendering += Tick; }
            if (_tris.Count == 0) Reinit();
            IsAnimating = true;
            double w = ActualWidth > 10 ? ActualWidth : 1100;
            double h = ActualHeight > 10 ? ActualHeight : 650;
            int total = _tris.Count; int done = 0;
            for (int i = 0; i < _tris.Count; i++)
            {
                var t = _tris[i];
                t.Shape.Visibility = Visibility.Collapsed; t.Dissolved = true;
                int delay = i * 50 + _rng.Next(60);
                Materialize(t, _rng.NextDouble() * w, _rng.NextDouble() * h, delay, () =>
                { done++; if (done >= total) { IsAnimating = false; onComplete?.Invoke(); } });
            }
        }

        public void FadeOut(Action? onComplete = null)
        {
            CancelAll();
            IsAnimating = true;
            int total = _tris.Count; int done = 0;
            for (int i = 0; i < _tris.Count; i++)
            {
                var t = _tris[i];
                int delay = i * 40 + _rng.Next(50);
                Dissolve(t, delay, () =>
                {
                    done++;
                    if (done >= total)
                    {
                        IsAnimating = false;
                        _active = false; _paused = false;
                        CompositionTarget.Rendering -= Tick;
                        Visibility = Visibility.Collapsed;
                        foreach (var tr in _tris)
                        {
                            tr.Shape.BeginAnimation(OpacityProperty, null);
                            tr.Sc.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                            tr.Sc.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                            tr.Shape.OpacityMask = null;
                            tr.Sc.ScaleX = 1; tr.Sc.ScaleY = 1; tr.Shape.Opacity = 1;
                            tr.Shape.Visibility = Visibility.Visible; tr.Dissolved = false; tr.Fading = false;
                        }
                        onComplete?.Invoke();
                    }
                });
            }
        }

        public void Burst(Point click)
        {
            if (IsAnimating) return;
            double w = ActualWidth > 10 ? ActualWidth : 1100;
            double h = ActualHeight > 10 ? ActualHeight : 650;
            foreach (var t in _tris)
            {
                double dx = t.X - click.X, dy = t.Y - click.Y;
                double dist = Math.Sqrt(dx * dx + dy * dy);
                if (dist < 300 && dist > 1)
                {
                    double force = (300 - dist) / 300 * 4;
                    if (!t.Fading && !t.Dissolved)
                    {
                        t.VX += dx / dist * force;
                        t.VY += dy / dist * force;
                        t.RotV += (_rng.NextDouble() - 0.5) * force * 2;
                    }

                    if (dist < 130 && !t.Dissolved && !t.Fading)
                    {
                        double nx = _rng.NextDouble() * w;
                        double ny = _rng.NextDouble() * h;
                        int reformDelay = 900 + _rng.Next(600);
                        Tri ct = t;
                        Dissolve(t, 0, () =>
                        {
                            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(reformDelay) };
                            _timers.Add(timer);
                            timer.Tick += (s, e) => { timer.Stop(); Materialize(ct, nx, ny, 0, null); };
                            timer.Start();
                        });
                    }
                }
            }
        }
    }
}
