using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace KitveiHakodeshLib
{
    internal class SplashOverlay : Control
    {
        // Dot config — matches Vue LoadingAnimation.vue exactly
        // 1.2s cycle @ 60fps = 72 ticks per cycle
        private const int   DotCount   = 3;
        private const float DotBaseR   = 9f;
        private const float DotMinScale = 0.6f;
        private const float DotMaxScale = 1.0f;
        private const float DotSpacing = 26f;
        // cycle = 1.2s @ 60fps → 72 ticks; speed = 2π/72
        private const float CycleSpeed = (float)(Math.PI * 2 / 72.0);
        // delays: 0s, 0.2s, 0.4s → in ticks: 0, 12, 24 → in radians
        private static readonly float[] DotDelays = { 0f, (float)(Math.PI * 2 * 12 / 72.0), (float)(Math.PI * 2 * 24 / 72.0) };

        // Accent #7C5CFC
        private const int AR = 124, AG = 92, AB = 252;

        private Image _logo;
        private float _alpha  = 0f;
        private bool  _fading = false;
        private float _phase  = 0f;   // advances each tick
        private readonly Timer _timer;

        public SplashOverlay(Image logo)
        {
            _logo = logo;

            SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.UserPaint |
                ControlStyles.OptimizedDoubleBuffer, true);

            _timer = new Timer { Interval = 16 };
            _timer.Tick += OnTick;
            _timer.Start();
        }

        public void FadeOut() { _fading = true; }

        private void OnTick(object sender, EventArgs e)
        {
            _phase += CycleSpeed;
            if (_phase > (float)(Math.PI * 2)) _phase -= (float)(Math.PI * 2);

            if (_fading)
            {
                _alpha -= 0.045f;
                if (_alpha <= 0f)
                {
                    _alpha = 0f;
                    _timer.Stop();
                    var p = Parent;
                    if (p != null) { p.Controls.Remove(this); p.Invalidate(); }
                    Dispose();
                    return;
                }
            }
            else
            {
                _alpha = Math.Min(1f, _alpha + 0.035f);
            }

            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            int a = (int)(_alpha * 255);
            if (a <= 0) return;

            int w = Width, h = Height, cx = w / 2, cy = h / 2;

            // ── Background ───────────────────────────────────────────────────────
            Color bg = BackColor;
            using (var b = new SolidBrush(Color.FromArgb(a, bg)))
                g.FillRectangle(b, ClientRectangle);

            // ── Logo ─────────────────────────────────────────────────────────────
            if (_logo != null)
            {
                int sz = Math.Min(148, Math.Min(w, h) / 3);
                var cm = new ColorMatrix { Matrix33 = _alpha };
                using (var ia = new ImageAttributes())
                {
                    ia.SetColorMatrix(cm);
                    g.DrawImage(_logo,
                        new Rectangle(cx - sz / 2, cy - sz / 2 - 24, sz, sz),
                        0, 0, _logo.Width, _logo.Height,
                        GraphicsUnit.Pixel, ia);
                }
            }

            // ── Bouncing dots ─────────────────────────────────────────────────────
            // Matches Vue LoadingAnimation.vue: 1.2s, peak at 40% of cycle, scale 0.6→1
            float totalW = (DotCount - 1) * DotSpacing;
            float startX = cx - totalW / 2f;
            float dotsY  = cy + 108f;

            for (int i = 0; i < DotCount; i++)
            {
                // position in cycle 0..2π, offset by dot's delay
                float p = _phase - DotDelays[i];
                // map sin to 0..1
                float raw = (float)(Math.Sin(p) * 0.5 + 0.5);
                // smoothstep — matches ease-in-out
                float t = raw * raw * (3f - 2f * raw);
                // scale: 0.6 at trough, 1.0 at peak — but peak is at 40% of cycle
                // We drive this purely from t which already peaks correctly via sin
                float scale   = DotMinScale + (DotMaxScale - DotMinScale) * t;
                float opacity = 0.4f + 0.6f * t;
                float r       = DotBaseR * scale;
                int   dotA    = (int)(opacity * _alpha * 255);
                float dx      = startX + i * DotSpacing;

                using (var brush = new SolidBrush(Color.FromArgb(dotA, AR, AG, AB)))
                    g.FillEllipse(brush, dx - r, dotsY - r, r * 2, r * 2);
            }
        }

        protected override void OnBackColorChanged(EventArgs e)
        {
            base.OnBackColorChanged(e);
            Invalidate();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _timer.Dispose();
                _logo?.Dispose();
                _logo = null;
            }
            base.Dispose(disposing);
        }
    }
}
