using System.Drawing;
using System.Windows.Forms;

namespace UpdateCheckerLib
{
    /// <summary>
    /// Topmost "update ready" dialog with standard Windows chrome.
    /// TopMost = true ensures the user never misses it behind Word or other windows.
    /// Built entirely in code — no designer file.
    /// </summary>
    public sealed class UpdateNotificationForm : Form
    {
        private UpdateNotificationForm(string message)
        {
            var accent = Color.FromArgb(0, 120, 212);

            Text              = "עדכון זמין - כלי קודש";
            FormBorderStyle   = FormBorderStyle.FixedDialog;
            StartPosition     = FormStartPosition.CenterScreen;
            TopMost           = true;
            RightToLeft       = RightToLeft.Yes;
            RightToLeftLayout = true;
            MinimizeBox       = false;
            MaximizeBox       = false;
            ShowInTaskbar     = false;
            BackColor         = Color.White;
            AutoSize          = true;
            AutoSizeMode      = AutoSizeMode.GrowAndShrink;
            Padding           = new Padding(24, 20, 24, 20);

            var layout = new TableLayoutPanel
            {
                AutoSize     = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount  = 1,
                RowCount     = 2,
                Dock         = DockStyle.Fill,
                Padding      = new Padding(0),
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

            var msgLabel = new Label
            {
                Text        = message,
                AutoSize    = true,
                MaximumSize = new Size(360, 0),
                Font        = new Font("Segoe UI", 10.5F),
                ForeColor   = Color.FromArgb(32, 32, 32),
                TextAlign   = ContentAlignment.MiddleCenter,
                Margin      = new Padding(0, 0, 0, 20),
                Anchor      = AnchorStyles.None,
            };

            var ok = new Button
            {
                Text      = "אישור",
                Font      = new Font("Segoe UI", 10F),
                Size      = new Size(100, 36),
                FlatStyle = FlatStyle.Flat,
                BackColor = accent,
                ForeColor = Color.White,
                Cursor    = Cursors.Hand,
                Anchor    = AnchorStyles.None,   // centres in the column
                DialogResult = DialogResult.OK,
            };
            ok.FlatAppearance.BorderSize         = 0;
            ok.FlatAppearance.MouseOverBackColor = Color.FromArgb(0, 102, 180);
            ok.FlatAppearance.MouseDownBackColor = Color.FromArgb(0, 84, 153);

            layout.Controls.Add(msgLabel, 0, 0);
            layout.Controls.Add(ok, 0, 1);
            Controls.Add(layout);
            AcceptButton = ok;
        }

        /// <summary>
        /// Shows the topmost update notification and blocks until the user dismisses it.
        /// Call on any UI thread — no special threading setup required.
        /// </summary>
        public static void Show(string message)
        {
            using (var form = new UpdateNotificationForm(message))
                form.ShowDialog();
        }
    }
}
