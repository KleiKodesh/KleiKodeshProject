using Microsoft.Web.WebView2.WinForms;
using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Zayit.Viewer
{
    public class DatabaseNotFoundDialog : Form
    {
        private readonly WebView2 _webView;

        public string SelectedDatabasePath { get; private set; }
        public bool ShouldDownloadZayit { get; private set; }

        public DatabaseNotFoundDialog(WebView2 webView = null)
        {
            _webView = webView;
            RightToLeft = RightToLeft.Yes;
            RightToLeftLayout = true;
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            SuspendLayout();

            // ===== Form =====
            StartPosition = FormStartPosition.CenterScreen;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            ShowInTaskbar = false;
            Font = new Font("Segoe UI", 10F);
            BackColor = Color.FromArgb(243, 244, 246);
            FormBorderStyle = FormBorderStyle.None;
            ControlBox = false;
            DoubleBuffered = true;
            TopMost = true;
            Opacity = 0.9;

            // ===== Main panel =====
            var mainPanel = new TableLayoutPanel
            {
                AutoSize = true,
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                Padding = new Padding(22, 28, 22, 22),
                BackColor = Color.White
            };

            // ===== Title =====
            var titleLabel = new Label
            {
                Text = "מסד הנתונים לא נמצא",
                Font = new Font("Segoe UI", 16F, FontStyle.Bold),
                ForeColor = Color.FromArgb(160, 0, 0),
                AutoSize = true,
                MaximumSize = new Size(420, 0),
                TextAlign = ContentAlignment.MiddleCenter,
                Anchor = AnchorStyles.None
            };

            // ===== Message =====
            var messageLabel = new Label
            {
                Text = "אנא בחר באחת מהאפשרויות הבאות",
                Font = new Font("Segoe UI", 10.5F),
                ForeColor = Color.DimGray,
                AutoSize = true,
                MaximumSize = new Size(420, 0),
                TextAlign = ContentAlignment.MiddleCenter,
                Margin = new Padding(0, 14, 0, 18),
                Anchor = AnchorStyles.None
            };

            // ===== Buttons =====
            var buttonsPanel = new TableLayoutPanel
            {
                AutoSize = true,
                ColumnCount = 2,
                Anchor = AnchorStyles.None
            };

            buttonsPanel.Controls.Add(
                CreateButton("הורד את הקובץ", Color.FromArgb(0, 120, 215), DownloadButton_Click), 0, 0);

            buttonsPanel.Controls.Add(
                CreateButton("בחר קובץ קיים", Color.FromArgb(76, 175, 80), BrowseButton_Click), 1, 0);

            mainPanel.Controls.Add(titleLabel);
            mainPanel.Controls.Add(messageLabel);
            mainPanel.Controls.Add(buttonsPanel);

            Controls.Add(mainPanel);

            // ===== Close button =====
            var closeButton = new Button
            {
                Font = new Font("Segoe MDL2 Assets", 7F),
                Text = "\uE8BB",
                Size = new Size(34, 34),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.Transparent,
                ForeColor = Color.SlateGray,
                TextAlign = ContentAlignment.MiddleCenter,
                TabStop = false,
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };

            closeButton.FlatAppearance.BorderSize = 0;
            closeButton.FlatAppearance.MouseOverBackColor = Color.FromArgb(232, 17, 35);
            closeButton.FlatAppearance.MouseDownBackColor = Color.FromArgb(220, 220, 220);
            closeButton.Click += (s, e) => Close();

            Controls.Add(closeButton);
            closeButton.BringToFront();

            Load += (s, e) =>
            {
                closeButton.Location = new Point(
                    ClientSize.Width - closeButton.Width - 6,
                    6);
            };

            // ===== Drag support =====
            MouseDown += DragForm;
            mainPanel.MouseDown += DragForm;

            ResumeLayout(false);
        }

        private static Button CreateButton(string text, Color color, EventHandler click)
        {
            var b = new Button
            {
                Text = text,
                AutoSize = true,
                Font = new Font("Segoe UI", 9F),
                Padding = new Padding(12, 6, 12, 6),
                BackColor = color,
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Margin = new Padding(6)
            };

            b.FlatAppearance.BorderSize = 0;
            b.Click += click;
            return b;
        }


        private void DownloadButton_Click(object sender, EventArgs e)
        {
            ShouldDownloadZayit = true;
            DialogResult = DialogResult.OK;
            Close();
        }

        private async void BrowseButton_Click(object sender, EventArgs e)
        {
            string path = null;

            if (_webView != null)
            {
                path = await WebViewDialogHelper.ShowOpenFileDialogAsync(
                    _webView,
                    "SQLite Database files (*.db)|*.db|All files (*.*)|*.*",
                    "בחר קובץ מסד נתונים");
            }
            else
            {
                using (var dlg = new OpenFileDialog())
                {
                    dlg.Filter = "SQLite Database files (*.db)|*.db|All files (*.*)|*.*";
                    if (dlg.ShowDialog() == DialogResult.OK)
                        path = dlg.FileName;
                }
            }

            if (!string.IsNullOrEmpty(path))
            {
                SelectedDatabasePath = path;
                DialogResult = DialogResult.OK;
                Close();
            }
        }

        // ===== Drag helpers =====
        [DllImport("user32.dll")] static extern bool ReleaseCapture();
        [DllImport("user32.dll")] static extern int SendMessage(IntPtr hWnd, int msg, int wParam, int lParam);
        const int WM_NCLBUTTONDOWN = 0xA1;
        const int HTCAPTION = 0x2;

        private void DragForm(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                ReleaseCapture();
                SendMessage(Handle, WM_NCLBUTTONDOWN, HTCAPTION, 0);
            }
        }
    }
}
