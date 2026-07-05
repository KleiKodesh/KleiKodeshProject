using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace KitveiHakodeshLib.FileSystemSearch
{
    /// <summary>
    /// RTL Hebrew dialog for managing user-defined excluded folders.
    /// The caller reads <see cref="ExcludedFolders"/> after DialogResult.OK.
    /// </summary>
    public sealed class ExcludedFoldersForm : Form
    {
        [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr h, int n);
        [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr h, int n, int v);
        private const int GWL_EXSTYLE      = -20;
        private const int WS_EX_RTLREADING = 0x00002000;
        private const int WS_EX_LAYOUTRTL  = 0x00400000;

        // ── Palette ───────────────────────────────────────────────────────────────

        private static readonly Color BgPage            = Color.FromArgb(249, 249, 249);
        private static readonly Color BgSurface         = Color.White;
        private static readonly Color BgBtnPrimary      = Color.FromArgb(0,   120, 212);
        private static readonly Color BgBtnPrimaryHover = Color.FromArgb(0,   102, 180);
        private static readonly Color BgBtnNeutral      = Color.FromArgb(255, 255, 255);
        private static readonly Color BgBtnNeutralHover = Color.FromArgb(243, 243, 243);
        private static readonly Color ColBorder         = Color.FromArgb(210, 210, 210);
        private static readonly Color ColBorderDanger   = Color.FromArgb(196,  43,  28);
        private static readonly Color ColTextPrimary    = Color.FromArgb( 32,  32,  32);
        private static readonly Color ColTextSecondary  = Color.FromArgb( 96,  96,  96);
        private static readonly Color ColTextDanger     = Color.FromArgb(196,  43,  28);

        // ── State ─────────────────────────────────────────────────────────────────

        private readonly List<string> _folders;
        private Font     _fontBody;
        private ListView _listView;
        private Button   _addButton;
        private Button   _deleteButton;
        private Button   _okButton;
        private Button   _cancelButton;

        public List<string> ExcludedFolders => new List<string>(_folders);

        // ── Constructor ───────────────────────────────────────────────────────────

        public ExcludedFoldersForm(IEnumerable<string> currentFolders)
        {
            _folders = new List<string>(currentFolders ?? new string[0]);
            BuildForm();
        }

        // ── Build ─────────────────────────────────────────────────────────────────

        private void BuildForm()
        {
            // Font created here on the UI thread to avoid GDI static-init crash
            _fontBody = new Font("Segoe UI", 9f, FontStyle.Regular, GraphicsUnit.Point);

            SuspendLayout();

            Text                = "תיקיות מוחרגות מחיפוש קבצים";
            Font                = _fontBody;
            BackColor           = BgPage;
            FormBorderStyle     = FormBorderStyle.FixedDialog;
            MaximizeBox         = false;
            MinimizeBox         = false;
            ShowInTaskbar       = false;
            StartPosition       = FormStartPosition.CenterParent;
            AutoScaleMode       = AutoScaleMode.Font;
            AutoScaleDimensions = new SizeF(6f, 13f);
            RightToLeft         = RightToLeft.Yes;
            RightToLeftLayout   = true;
            ClientSize          = new Size(460, 340);

            // ── Root layout ───────────────────────────────────────────────────────
            var root = new TableLayoutPanel
            {
                Dock        = DockStyle.Fill,
                ColumnCount = 1,
                RowCount    = 3,
                BackColor   = BgPage,
                Padding     = new Padding(11, 8, 11, 0),
                RightToLeft = RightToLeft.Yes,
            };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));   // description
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));  // list
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 43));   // footer

            // ── Description label ─────────────────────────────────────────────────
            var description = new Label
            {
                Text        = "תיקיות אלה יוחרגו מתוצאות חיפוש הקבצים",
                Font        = new Font("Segoe UI", 8.5f, FontStyle.Regular, GraphicsUnit.Point),
                ForeColor   = ColTextSecondary,
                Dock        = DockStyle.Fill,
                // With RightToLeftLayout=true on the form, coordinates are mirrored —
                // MiddleLeft physically renders on the right (visual start in RTL).
                TextAlign   = ContentAlignment.MiddleLeft,
                RightToLeft = RightToLeft.Yes,
            };

            // ── ListView ──────────────────────────────────────────────────────────
            _listView = new ListView
            {
                View              = View.Details,
                FullRowSelect     = true,
                GridLines         = false,
                HeaderStyle       = ColumnHeaderStyle.None,
                BorderStyle       = BorderStyle.FixedSingle,
                BackColor         = BgSurface,
                ForeColor         = ColTextPrimary,
                Font              = new Font("Segoe UI", 10.5f, FontStyle.Regular, GraphicsUnit.Point),
                Dock              = DockStyle.Fill,
                MultiSelect       = false,
                RightToLeft       = RightToLeft.Yes,
                RightToLeftLayout = true,
            };
            _listView.Columns.Add("נתיב", -2);
            _listView.SelectedIndexChanged += OnSelectionChanged;
            _listView.SizeChanged += (s, e) =>
            {
                if (_listView.Columns.Count > 0)
                    _listView.Columns[0].Width = _listView.ClientSize.Width;
            };

            foreach (string folder in _folders)
                _listView.Items.Add(new ListViewItem(folder));

            // ── Footer ────────────────────────────────────────────────────────────
            // Plain Panel with Anchor — the only reliable way to pin two button
            // groups to opposite edges without fighting RTL FlowLayout mirroring.
            // Anchor is physical (Left/Right = physical screen edges), unaffected
            // by RightToLeft/RightToLeftLayout.
            var footer = new Panel
            {
                Dock      = DockStyle.Fill,
                BackColor = BgPage,
                Padding   = new Padding(0, 0, 0, 0),
            };

            // Separator line at the top of the footer panel
            var separator = new Panel
            {
                Height    = 1,
                Dock      = DockStyle.Top,
                BackColor = ColBorder,
            };
            footer.Controls.Add(separator);

            _okButton     = MakePrimaryButton("אישור",        75);
            _cancelButton = MakeNeutralButton("ביטול",        75);
            _addButton    = MakePrimaryButton("הוסף תיקייה", 100);
            _deleteButton = MakeDangerButton ("הסר",          64);

            _okButton.DialogResult     = DialogResult.OK;
            _cancelButton.DialogResult = DialogResult.Cancel;
            _deleteButton.Enabled      = false;
            AcceptButton = _okButton;
            CancelButton = _cancelButton;

            _addButton.Click    += OnAddClicked;
            _deleteButton.Click += OnDeleteClicked;

            // Right edge (visual start in RTL): אישור rightmost, ביטול to its left
            _okButton.Anchor     = AnchorStyles.Top | AnchorStyles.Right;
            _cancelButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;

            // Left edge (visual end in RTL): הוסף תיקייה leftmost+1, הסר at left=0
            _deleteButton.Anchor = AnchorStyles.Top | AnchorStyles.Left;
            _addButton.Anchor    = AnchorStyles.Top | AnchorStyles.Left;

            // Position relative to footer. Footer width = form - 22px padding.
            // Use an event to position on first layout.
            footer.Layout += (s, e) =>
            {
                int w   = footer.ClientSize.Width;
                int top = 9;  // classic dialog: ~8–10px below separator
                int gap = 6;

                // Physical right edge: אישור then ביטול
                _okButton.Location     = new Point(w - _okButton.Width, top);
                _cancelButton.Location = new Point(w - _okButton.Width - gap - _cancelButton.Width, top);

                // Physical left edge: הסר then הוסף תיקייה
                _deleteButton.Location = new Point(0, top);
                _addButton.Location    = new Point(_deleteButton.Width + gap, top);
            };

            footer.Controls.Add(_okButton);
            footer.Controls.Add(_cancelButton);
            footer.Controls.Add(_addButton);
            footer.Controls.Add(_deleteButton);

            root.Controls.Add(description, 0, 0);
            root.Controls.Add(_listView,   0, 1);
            root.Controls.Add(footer,      0, 2);
            Controls.Add(root);
            ResumeLayout(false);
            PerformLayout();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            int ex = GetWindowLong(Handle, GWL_EXSTYLE);
            SetWindowLong(Handle, GWL_EXSTYLE, ex | WS_EX_RTLREADING | WS_EX_LAYOUTRTL);
        }

        // ── Button factories ──────────────────────────────────────────────────────

        private Button MakePrimaryButton(string text, int width)
        {
            var btn = new Button
            {
                Text      = text,
                Font      = _fontBody,
                ForeColor = Color.White,
                BackColor = BgBtnPrimary,
                FlatStyle = FlatStyle.Flat,
                Size      = new Size(width, 23),
                Margin    = new Padding(0, 0, 6, 0),
                Cursor    = Cursors.Hand,
                UseVisualStyleBackColor = false,
            };
            btn.FlatAppearance.BorderSize         = 0;
            btn.FlatAppearance.MouseOverBackColor = BgBtnPrimaryHover;
            btn.FlatAppearance.MouseDownBackColor = BgBtnPrimaryHover;
            return btn;
        }

        private Button MakeNeutralButton(string text, int width)
        {
            var btn = new Button
            {
                Text      = text,
                Font      = _fontBody,
                ForeColor = ColTextPrimary,
                BackColor = BgBtnNeutral,
                FlatStyle = FlatStyle.Flat,
                Size      = new Size(width, 23),
                Margin    = new Padding(0, 0, 6, 0),
                Cursor    = Cursors.Hand,
                UseVisualStyleBackColor = false,
            };
            btn.FlatAppearance.BorderSize         = 1;
            btn.FlatAppearance.BorderColor        = ColBorder;
            btn.FlatAppearance.MouseOverBackColor = BgBtnNeutralHover;
            btn.FlatAppearance.MouseDownBackColor = BgBtnNeutralHover;
            return btn;
        }

        private Button MakeDangerButton(string text, int width)
        {
            var btn = new Button
            {
                Text      = text,
                Font      = _fontBody,
                ForeColor = ColTextDanger,
                BackColor = BgBtnNeutral,
                FlatStyle = FlatStyle.Flat,
                Size      = new Size(width, 23),
                Margin    = new Padding(0, 0, 6, 0),
                Cursor    = Cursors.Hand,
                UseVisualStyleBackColor = false,
            };
            btn.FlatAppearance.BorderSize         = 1;
            btn.FlatAppearance.BorderColor        = ColBorderDanger;
            btn.FlatAppearance.MouseOverBackColor = Color.FromArgb(253, 231, 229);
            btn.FlatAppearance.MouseDownBackColor = Color.FromArgb(253, 231, 229);
            return btn;
        }

        // ── Event handlers ────────────────────────────────────────────────────────

        private void OnSelectionChanged(object sender, EventArgs e)
        {
            _deleteButton.Enabled = _listView.SelectedItems.Count > 0;
        }

        private void OnAddClicked(object sender, EventArgs e)
        {
            using (var dlg = new FolderBrowserDialog())
            {
                dlg.Description         = "בחר תיקייה להחרגה מחיפוש הקבצים";
                dlg.ShowNewFolderButton = false;

                if (dlg.ShowDialog(this) != DialogResult.OK) return;

                string selected = dlg.SelectedPath;
                if (string.IsNullOrEmpty(selected)) return;

                foreach (ListViewItem existing in _listView.Items)
                {
                    if (string.Equals(existing.Text, selected, StringComparison.OrdinalIgnoreCase))
                    {
                        existing.Selected = true;
                        _listView.EnsureVisible(existing.Index);
                        return;
                    }
                }

                var item = new ListViewItem(selected) { Selected = true };
                _listView.Items.Add(item);
                _folders.Add(selected);
                _listView.EnsureVisible(item.Index);
            }
        }

        private void OnDeleteClicked(object sender, EventArgs e)
        {
            if (_listView.SelectedItems.Count == 0) return;
            var item = _listView.SelectedItems[0];
            _folders.Remove(item.Text);
            item.Remove();
            _deleteButton.Enabled = false;
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (DialogResult == DialogResult.OK)
            {
                _folders.Clear();
                foreach (ListViewItem item in _listView.Items)
                    _folders.Add(item.Text);
            }
            base.OnFormClosing(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                _fontBody?.Dispose();
            base.Dispose(disposing);
        }
    }
}
