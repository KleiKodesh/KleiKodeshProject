using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace Zayit.Viewer
{
    public partial class DatabaseNotFoundDialog : Form
    {
        public string SelectedDatabasePath { get; private set; }
        public bool ShouldDownloadZayit { get; private set; }

        public DatabaseNotFoundDialog()
        {
            InitializeComponent();
            this.RightToLeft = RightToLeft.Yes;
            this.RightToLeftLayout = true;
        }

        private void InitializeComponent()
        {
            this.SuspendLayout();

            // Form properties
            this.Text = "מסד נתונים לא נמצא - Zayit";
            this.MinimumSize = new Size(450, 250);
            this.AutoSize = true;
            this.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            this.StartPosition = FormStartPosition.CenterParent;
            this.FormBorderStyle = FormBorderStyle.Sizable;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.ShowInTaskbar = false;
            this.Font = new Font("Segoe UI", 9F, FontStyle.Regular);

            // Main panel
            var mainPanel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                RowCount = 3,
                ColumnCount = 1,
                Padding = new Padding(20),
                BackColor = Color.White,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink
            };

            // Icon and title
            var titlePanel = new Panel 
            { 
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                MinimumSize = new Size(400, 60)
            };
            var iconLabel = new Label
            {
                Text = "⚠️",
                Font = new Font("Segoe UI", 24F),
                ForeColor = Color.Orange,
                AutoSize = true,
                TextAlign = ContentAlignment.MiddleCenter,
                Anchor = AnchorStyles.None
            };
            var titleLabel = new Label
            {
                Text = "מסד הנתונים לא נמצא",
                Font = new Font("Segoe UI", 14F, FontStyle.Bold),
                AutoSize = true,
                MaximumSize = new Size(350, 0),
                ForeColor = Color.DarkRed,
                TextAlign = ContentAlignment.MiddleCenter,
                Anchor = AnchorStyles.None
            };
            
            // Create a horizontal layout for icon and title
            var titleLayoutPanel = new TableLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 2,
                RowCount = 1,
                Dock = DockStyle.Fill
            };
            titleLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            titleLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            titleLayoutPanel.Controls.Add(iconLabel, 0, 0);
            titleLayoutPanel.Controls.Add(titleLabel, 1, 0);
            
            titlePanel.Controls.Add(titleLayoutPanel);

            // Message
            var messageLabel = new Label
            {
                Text = "לא נמצא קובץ מסד הנתונים במיקום הצפוי:\n" +
                       "%AppData%\\Roaming\\io.github.kdroidfilter.seforimapp\\databases\\seforim.db\n\n" +
                       "אנא בחר באחת מהאפשרויות הבאות:",
                Font = new Font("Segoe UI", 10F),
                ForeColor = Color.Black,
                TextAlign = ContentAlignment.MiddleCenter,
                AutoSize = true,
                MaximumSize = new Size(400, 0),
                Padding = new Padding(10),
                MinimumSize = new Size(400, 80),
                Anchor = AnchorStyles.None
            };

            // Buttons panel - only 2 buttons in a single row
            var buttonsPanel = new TableLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 2,
                RowCount = 1,
                MinimumSize = new Size(400, 50),
                Padding = new Padding(10),
                Anchor = AnchorStyles.None
            };

            var downloadButton = new Button
            {
                Text = "הורד את אפליקציית Zayit",
                Font = new Font("Segoe UI", 10F),
                Height = 35,
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(0, 120, 215),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Margin = new Padding(5),
                TextAlign = ContentAlignment.MiddleCenter
            };
            downloadButton.FlatAppearance.BorderSize = 0;
            downloadButton.Click += DownloadButton_Click;

            var browseButton = new Button
            {
                Text = "בחר קובץ מסד נתונים מהמחשב",
                Font = new Font("Segoe UI", 10F),
                Height = 35,
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(76, 175, 80),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Margin = new Padding(5),
                TextAlign = ContentAlignment.MiddleCenter
            };
            browseButton.FlatAppearance.BorderSize = 0;
            browseButton.Click += BrowseButton_Click;

            // Add buttons to panel - only 2 buttons in a single row
            buttonsPanel.Controls.Add(downloadButton, 0, 0);
            buttonsPanel.Controls.Add(browseButton, 1, 0);

            // Set column styles for equal width
            buttonsPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            buttonsPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            buttonsPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            // Add all panels to main panel with centering
            mainPanel.Controls.Add(titlePanel, 0, 0);
            mainPanel.Controls.Add(messageLabel, 0, 1);
            mainPanel.Controls.Add(buttonsPanel, 0, 2);
            
            // Center all controls in their cells
            foreach (Control control in mainPanel.Controls)
            {
                control.Anchor = AnchorStyles.None;
            }

            // Set row styles for dynamic sizing
            mainPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            mainPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            mainPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            this.Controls.Add(mainPanel);
            this.ResumeLayout(false);
        }

        private void DownloadButton_Click(object sender, EventArgs e)
        {
            ShouldDownloadZayit = true;
            this.DialogResult = DialogResult.OK;
            this.Close();

            // Open Zayit download page
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "https://github.com/kdroidFilter/zayit/releases",
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"לא ניתן לפתוח את הדפדפן: {ex.Message}", "שגיאה", 
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void BrowseButton_Click(object sender, EventArgs e)
        {
            using (var dialog = new OpenFileDialog())
            {
                dialog.Filter = "SQLite Database files (*.db)|*.db|All files (*.*)|*.*";
                dialog.Title = "בחר קובץ מסד נתונים";
                dialog.CheckFileExists = true;

                if (dialog.ShowDialog() == DialogResult.OK)
                {
                    SelectedDatabasePath = dialog.FileName;
                    this.DialogResult = DialogResult.OK;
                    this.Close();
                }
            }
        }
    }
}