using System;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace UpdateCheckerLib
{
    internal sealed class DownloadProgressForm : Form
    {
        private readonly ProgressBar _bar;
        private readonly Label _label;
        private readonly Button _cancel;
        private readonly Panel _titlePanel;
        private readonly Label _titleLabel;

        public CancellationTokenSource Cancellation { get; }
        public bool IsCancelled => Cancellation.IsCancellationRequested;

        private DownloadProgressForm()
        {
            // Modern colors
            var backgroundColor = Color.FromArgb(250, 250, 250);
            var accentColor = Color.FromArgb(0, 120, 212);
            var textColor = Color.FromArgb(32, 32, 32);
            var borderColor = Color.FromArgb(200, 200, 200);

            Width = 480;
            Height = 220;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterScreen;
            RightToLeft = RightToLeft.Yes;
            RightToLeftLayout = true;
            BackColor = backgroundColor;
            Padding = new Padding(1);

            // Add shadow effect via border
            Paint += (s, e) =>
            {
                ControlPaint.DrawBorder(e.Graphics, ClientRectangle,
                    borderColor, ButtonBorderStyle.Solid);
            };

            // Custom title bar
            _titlePanel = new Panel
            {
                Dock = DockStyle.Top,
                Height = 50,
                BackColor = backgroundColor,
                Padding = new Padding(20, 15, 20, 10),
                RightToLeft = RightToLeft.Yes
            };

            _titleLabel = new Label
            {
                Text = "הורדת עדכון",
                Dock = DockStyle.Fill,
                Font = new Font("Segoe UI", 14F, FontStyle.Regular),
                ForeColor = textColor,
                TextAlign = ContentAlignment.MiddleRight,
                RightToLeft = RightToLeft.Yes
            };

            _titlePanel.Controls.Add(_titleLabel);

            // Make title bar draggable
            _titlePanel.MouseDown += TitlePanel_MouseDown;
            _titleLabel.MouseDown += TitlePanel_MouseDown;

            // Content area
            var contentPanel = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(25, 15, 25, 20),
                BackColor = backgroundColor,
                RightToLeft = RightToLeft.Yes
            };

            _label = new Label
            {
                Dock = DockStyle.Top,
                Height = 45,
                Font = new Font("Segoe UI", 11F),
                ForeColor = textColor,
                TextAlign = ContentAlignment.MiddleCenter,
                Padding = new Padding(0, 10, 0, 10),
                RightToLeft = RightToLeft.Yes
            };

            _bar = new ProgressBar
            {
                Dock = DockStyle.Top,
                Height = 28,
                ForeColor = accentColor,
                Margin = new Padding(0, 10, 0, 0),
                RightToLeft = RightToLeft.Yes,
                RightToLeftLayout = true
            };

            _cancel = new Button
            {
                Dock = DockStyle.Bottom,
                Text = "ביטול",
                Height = 44,
                Font = new Font("Segoe UI", 11F),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.White,
                ForeColor = textColor,
                Cursor = Cursors.Hand,
                Margin = new Padding(0, 20, 0, 0),
                RightToLeft = RightToLeft.Yes
            };

            _cancel.FlatAppearance.BorderColor = borderColor;
            _cancel.FlatAppearance.BorderSize = 1;
            _cancel.FlatAppearance.MouseOverBackColor = Color.FromArgb(240, 240, 240);
            _cancel.FlatAppearance.MouseDownBackColor = Color.FromArgb(230, 230, 230);

            contentPanel.Controls.Add(_cancel);
            contentPanel.Controls.Add(_bar);
            contentPanel.Controls.Add(_label);

            Controls.Add(contentPanel);
            Controls.Add(_titlePanel);

            Cancellation = new CancellationTokenSource();
            _cancel.Click += (_, __) => Cancellation.Cancel();
            FormClosing += (_, __) => Cancellation.Cancel();
        }

        // Enable dragging the form by the title bar
        private Point _mouseOffset;
        private void TitlePanel_MouseDown(object sender, MouseEventArgs e)
        {
            _mouseOffset = new Point(-e.X, -e.Y);
            _titlePanel.MouseMove += TitlePanel_MouseMove;
            _titlePanel.MouseUp += TitlePanel_MouseUp;
            _titleLabel.MouseMove += TitlePanel_MouseMove;
            _titleLabel.MouseUp += TitlePanel_MouseUp;
        }

        private void TitlePanel_MouseMove(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                Point mousePos = Control.MousePosition;
                mousePos.Offset(_mouseOffset.X, _mouseOffset.Y);
                Location = mousePos;
            }
        }

        private void TitlePanel_MouseUp(object sender, MouseEventArgs e)
        {
            _titlePanel.MouseMove -= TitlePanel_MouseMove;
            _titlePanel.MouseUp -= TitlePanel_MouseUp;
            _titleLabel.MouseMove -= TitlePanel_MouseMove;
            _titleLabel.MouseUp -= TitlePanel_MouseUp;
        }

        public static DownloadProgressForm ShowModeless(string version)
        {
            DownloadProgressForm form = null;
            var thread = new Thread(() =>
            {
                form = new DownloadProgressForm();
                form._label.Text = $"מוריד גרסה {version}...";
                Application.Run(form);
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.IsBackground = true;
            thread.Start();

            while (form == null || !form.IsHandleCreated)
                Thread.Sleep(10);

            return form;
        }

        public void SetIndeterminate(string text)
        {
            SafeInvoke(() =>
            {
                _label.Text = text;
                _bar.Style = ProgressBarStyle.Marquee;
            });
        }

        public void UpdateProgress(int percent, string text)
        {
            SafeInvoke(() =>
            {
                _label.Text = text;
                _bar.Style = ProgressBarStyle.Continuous;
                _bar.Value = Math.Max(0, Math.Min(100, percent));
            });
        }

        public void SafeClose()
        {
            SafeInvoke(Close);
        }

        private void SafeInvoke(Action action)
        {
            if (IsDisposed) return;
            if (InvokeRequired) BeginInvoke(action);
            else action();
        }
    }
}