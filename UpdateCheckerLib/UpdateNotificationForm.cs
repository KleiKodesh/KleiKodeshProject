using System;
using System.Drawing;
using System.Windows.Forms;

namespace UpdateCheckerLib
{
    /// <summary>
    /// Minimal topmost dialog for "update ready" notifications.
    /// TopMost = true ensures the user never misses it behind Word or other windows.
    /// Built entirely in code — no designer file.
    /// </summary>
    internal sealed class UpdateNotificationForm : Form
    {
        private UpdateNotificationForm(string message)
        {
            var backgroundColor = Color.FromArgb(250, 250, 250);
            var accentColor     = Color.FromArgb(0, 120, 212);
            var textColor       = Color.FromArgb(32, 32, 32);
            var borderColor     = Color.FromArgb(200, 200, 200);

            Width               = 420;
            Height              = 180;
            FormBorderStyle     = FormBorderStyle.None;
            StartPosition       = FormStartPosition.CenterScreen;
            TopMost             = true;
            RightToLeft         = RightToLeft.Yes;
            RightToLeftLayout   = true;
            BackColor           = backgroundColor;

            // Border
            Paint += (s, e) =>
                ControlPaint.DrawBorder(e.Graphics, ClientRectangle, borderColor, ButtonBorderStyle.Solid);

            // Title bar
            var titlePanel = new Panel
            {
                Dock      = DockStyle.Top,
                Height    = 44,
                BackColor = accentColor,
                Padding   = new Padding(16, 0, 16, 0),
            };
            var titleLabel = new Label
            {
                Text      = "עדכון זמין - כלי קודש",
                Dock      = DockStyle.Fill,
                Font      = new Font("Segoe UI", 11F, FontStyle.Regular),
                ForeColor = Color.White,
                TextAlign = ContentAlignment.MiddleRight,
            };
            titlePanel.Controls.Add(titleLabel);

            // Message
            var msgLabel = new Label
            {
                Text      = message,
                Dock      = DockStyle.Fill,
                Font      = new Font("Segoe UI", 10F),
                ForeColor = textColor,
                TextAlign = ContentAlignment.MiddleCenter,
                Padding   = new Padding(16, 8, 16, 0),
            };

            // OK button
            var ok = new Button
            {
                Text      = "אישור",
                Dock      = DockStyle.Bottom,
                Height    = 40,
                Font      = new Font("Segoe UI", 10F),
                FlatStyle = FlatStyle.Flat,
                BackColor = accentColor,
                ForeColor = Color.White,
                Cursor    = Cursors.Hand,
                DialogResult = DialogResult.OK,
            };
            ok.FlatAppearance.BorderSize = 0;
            ok.FlatAppearance.MouseOverBackColor = Color.FromArgb(0, 102, 180);
            ok.FlatAppearance.MouseDownBackColor = Color.FromArgb(0, 84, 153);

            Controls.Add(msgLabel);
            Controls.Add(ok);
            Controls.Add(titlePanel);

            AcceptButton = ok;
        }

        /// <summary>
        /// Shows the topmost update notification and blocks until the user clicks OK.
        /// Must be called on an STA thread with a message pump (or the VSTO STA thread).
        /// </summary>
        public static void Show(string message)
        {
            using (var form = new UpdateNotificationForm(message))
                form.ShowDialog();
        }
    }
}
