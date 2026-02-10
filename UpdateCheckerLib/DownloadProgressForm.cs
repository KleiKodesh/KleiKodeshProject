using System;
using System.Threading;
using System.Windows.Forms;

namespace UpdateCheckerLib
{
    internal sealed class DownloadProgressForm : Form
    {
        private readonly ProgressBar _bar;
        private readonly Label _label;
        private readonly Button _cancel;

        public CancellationTokenSource Cancellation { get; }
        public bool IsCancelled => Cancellation.IsCancellationRequested;

        private DownloadProgressForm()
        {
            Text = "הורדת עדכון";
            Width = 420;
            Height = 140;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            RightToLeft = RightToLeft.Yes;
            RightToLeftLayout = true;

            _label = new Label
            {
                Dock = DockStyle.Top,
                Height = 35,
                TextAlign = System.Drawing.ContentAlignment.MiddleCenter
            };

            _bar = new ProgressBar
            {
                Dock = DockStyle.Top,
                Height = 20
            };

            _cancel = new Button
            {
                Dock = DockStyle.Bottom,
                Text = "ביטול",
                Height = 30
            };

            Controls.Add(_cancel);
            Controls.Add(_bar);
            Controls.Add(_label);

            Cancellation = new CancellationTokenSource();

            _cancel.Click += (_, __) => Cancellation.Cancel();
            FormClosing += (_, __) => Cancellation.Cancel();
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
