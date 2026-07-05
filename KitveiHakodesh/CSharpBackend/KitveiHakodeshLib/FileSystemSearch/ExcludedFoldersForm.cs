using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace KitveiHakodeshLib.FileSystemSearch
{
    /// <summary>
    /// RTL Hebrew dialog for managing user-defined excluded folders.
    ///
    /// The caller passes in the current list and reads <see cref="ExcludedFolders"/>
    /// after <see cref="DialogResult.OK"/> to get the updated list.
    ///
    /// Design: Windows 11 Fluent light theme — flat list rows, 4px border-radius
    /// buttons, Segoe UI Variable typeface, compact 44px-touch-target controls.
    /// </summary>
    public sealed class ExcludedFoldersForm : Form
    {
        // ── Win32 helpers for RTL title bar ──────────────────────────────────────

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        private const int GWL_EXSTYLE   = -20;
        private const int WS_EX_RTLREADING  = 0x00002000;
        private const int WS_EX_LAYOUTRTL   = 0x00400000;
        private const int WS_EX_NOINHERITLAYOUT = 0x00100000;

        // ── Palette (Fluent light) ────────────────────────────────────────────────

        private static readonly Color ColorBackground     = Color.FromArgb(243, 243, 243);
        private static readonly Color ColorSurface        = Color.White;
        private static readonly Color ColorBorder         = Color.FromArgb(229, 229, 229);
        private static readonly Color ColorTextPrimary    = Color.FromArgb(32,  32,  32);
        private static readonly Color ColorTextSecondary  = Color.FromArgb(100, 100, 100);
        private static readonly Color ColorAccent         = Color.FromArgb(0,   120, 212);
        private static readonly Color ColorAccentHover    = Color.FromArgb(0,   102, 180);
        private static readonly Color ColorDeleteHover    = Color.FromArgb(196,  43,  28);
        private static readonly Color ColorDeleteBorder   = Color.FromArgb(196,  43,  28);
        private static readonly Color ColorListHover      = Color.FromArgb(245, 245, 245);
        private static readonly Color ColorListSelected   = Color.FromArgb(221, 235, 247);
        private static readonly Font  FontUi              = new Font("Segoe UI Variable", 12f, FontStyle.Regular, GraphicsUnit.Pixel);
        private static readonly Font  FontSmall           = new Font("Segoe UI Variable", 11f, FontStyle.Regular, GraphicsUnit.Pixel);
        private static readonly Font  FontTitle           = new Font("Segoe UI Variable", 13f, FontStyle.Regular, GraphicsUnit.Pixel);

        // ── State ─────────────────────────────────────────────────────────────────

        private readonly List<string> _folders;
        private ListView _listView;
        private Button   _addButton;
        private Button   _deleteButton;
        private Button   _okButton;
        private Button   _cancelButton;

        /// <summary>The updated list of excluded folder paths after the user clicks OK.</summary>
        public List<string> ExcludedFolders => new List<string>(_folders);

        // ── Construction ──────────────────────────────────────────────────────────

        public ExcludedFoldersForm(IEnumerable<string> currentFolders)
        {
            _folders = new List<string>(currentFolders ?? new string[0]);
            BuildForm();
        }

        private void BuildForm()
        {
            // ── Form properties ───────────────────────────────────────────────────
            Text             = "תיקיות מוחרגות מחיפוש קבצים";
            RightToLeft      = RightToLeft.Yes;
            RightToLeftLayout = true;
            Font             = FontUi;
            BackColor        = ColorBackground;
            ClientSize       = new Size(520, 420);
            FormBorderStyle  = FormBorderStyle.FixedDialog;
            MaximizeBox      = false;
            MinimizeBox      = false;
            ShowInTaskbar    = false;
            StartPosition    = FormStartPosition.CenterParent;
            Padding          = new Padding(0);

            // ── Description label ─────────────────────────────────────────────────
            var descriptionLabel = new Label
            {
                Text      = "תיקיות אלה יוחרגו מתוצאות חיפוש הקבצים. השינויים נכנסים לתוקף מיד — אין צורך לבנות מחדש את האינדקס.",
                Font      = FontSmall,
                ForeColor = ColorTextSecondary,
                AutoSize  = false,
                Size      = new Size(484, 36),
                Location  = new Point(18, 14),
                TextAlign = ContentAlignment.MiddleRight,
            };

            // ── List view ─────────────────────────────────────────────────────────
            _listView = new ListView
            {
                View           = View.Details,
                FullRowSelect   = true,
                GridLines       = false,
                HeaderStyle     = ColumnHeaderStyle.None,
                BorderStyle     = BorderStyle.FixedSingle,
                BackColor       = ColorSurface,
                ForeColor       = ColorTextPrimary,
                Font            = FontUi,
                Location        = new Point(18, 58),
                Size            = new Size(484, 282),
                MultiSelect     = false,
                OwnerDraw       = true,
                RightToLeft     = RightToLeft.Yes,
                RightToLeftLayout = true,
            };

            _listView.Columns.Add("נתיב", 480);
            _listView.DrawColumnHeader += (s, e) => e.DrawDefault = false;
            _listView.DrawItem         += OnDrawListItem;
            _listView.DrawSubItem      += OnDrawListSubItem;
            _listView.SelectedIndexChanged += OnSelectionChanged;

            foreach (string folder in _folders)
                _listView.Items.Add(new ListViewItem(folder));

            // ── Action buttons (top row: Add + Delete) ────────────────────────────
            _addButton = MakeButton("+ הוסף תיקייה", ColorAccent, Color.White, ColorAccentHover);
            _addButton.Location = new Point(18, 352);
            _addButton.Size     = new Size(140, 36);
            _addButton.Click   += OnAddClicked;

            _deleteButton = MakeButton("הסר", ColorSurface, ColorDeleteHover, ColorSurface);
            _deleteButton.Location   = new Point(168, 352);
            _deleteButton.Size       = new Size(90, 36);
            _deleteButton.ForeColor  = ColorDeleteHover;
            _deleteButton.FlatAppearance.BorderColor = ColorDeleteBorder;
            _deleteButton.Enabled    = false;
            _deleteButton.Click     += OnDeleteClicked;

            // ── Dialog buttons (OK + Cancel) ──────────────────────────────────────
            _cancelButton = MakeButton("ביטול", ColorSurface, ColorTextPrimary, ColorListHover);
            _cancelButton.Location  = new Point(18, 368);
            _cancelButton.Size      = new Size(90, 36);
            _cancelButton.DialogResult = DialogResult.Cancel;
            CancelButton = _cancelButton;

            _okButton = MakeButton("אישור", ColorAccent, Color.White, ColorAccentHover);
            _okButton.Location    = new Point(118, 368);
            _okButton.Size        = new Size(90, 36);
            _okButton.DialogResult = DialogResult.OK;
            AcceptButton = _okButton;

            // Re-layout: action buttons on the right, OK/Cancel on the left.
            // (In RTL the visual right = inline-start = x near 0 in client coordinates
            // because RightToLeftLayout mirrors coordinates.)
            // Lay things out left-to-right in physical client coords:
            //   physical left = visually right in RTL layout.
            //   18..108  = ביטול  (Cancel, less important)
            //  118..208  = אישור  (OK, primary)
            //  gap
            //  [flexible]
            //  340..430  = הסר   (Delete)
            //  340..484 handled by reflow below
            //
            // Re-do layout without RTL coordinate flip confusion:
            // Place controls by hand in client pixels from left edge.

            _cancelButton.Location = new Point(18, 368);
            _okButton.Location     = new Point(118, 368);

            // "הסר" and "+ הוסף תיקייה" — keep them on the physical right side (high X).
            _deleteButton.Location = new Point(374, 368);
            _addButton.Location    = new Point(362 - 140 - 10, 368);   // 212, 368

            // Re-center the whole footer: Cancel | OK | [gap] | + Add | Delete
            // Physical layout (left→right):
            //   18  Cancel(90)  118  OK(90)  [gap]  [250 Add(140)]  [400 Del(90)]  490
            _cancelButton.Location = new Point(18,  368);
            _okButton.Location     = new Point(118, 368);
            _addButton.Location    = new Point(248, 368);
            _deleteButton.Location = new Point(400, 368);

            _addButton.Size    = new Size(144, 36);
            _deleteButton.Size = new Size(100, 36);
            _okButton.Size     = new Size(100, 36);
            _cancelButton.Size = new Size(100, 36);

            // ── Separator line ────────────────────────────────────────────────────
            var separator = new Panel
            {
                BackColor = ColorBorder,
                Location  = new Point(0, 356),
                Size      = new Size(520, 1),
            };

            Controls.AddRange(new Control[]
            {
                descriptionLabel,
                _listView,
                separator,
                _cancelButton,
                _okButton,
                _addButton,
                _deleteButton,
            });
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            // Force RTL on the title bar via extended window style.
            int exStyle = GetWindowLong(Handle, GWL_EXSTYLE);
            exStyle |= WS_EX_RTLREADING | WS_EX_LAYOUTRTL | WS_EX_NOINHERITLAYOUT;
            SetWindowLong(Handle, GWL_EXSTYLE, exStyle);
        }

        // ── Owner-draw list ───────────────────────────────────────────────────────

        private void OnDrawListItem(object sender, DrawListViewItemEventArgs e)
        {
            bool selected = e.Item.Selected;
            var  back     = selected ? ColorListSelected : ColorSurface;
            using (var brush = new SolidBrush(back))
                e.Graphics.FillRectangle(brush, e.Bounds);
        }

        private void OnDrawListSubItem(object sender, DrawListViewSubItemEventArgs e)
        {
            bool selected = e.Item.Selected;
            var  back     = selected ? ColorListSelected : ColorSurface;

            using (var brush = new SolidBrush(back))
                e.Graphics.FillRectangle(brush, e.Bounds);

            // Row height: 44px for comfortable touch target.
            // Center text vertically.
            var textBounds = new RectangleF(
                e.Bounds.X + 10,
                e.Bounds.Y + (e.Bounds.Height - FontUi.Height) / 2f,
                e.Bounds.Width - 20,
                FontUi.Height + 4);

            StringFormat format = new StringFormat
            {
                Alignment     = StringAlignment.Far,   // right-align in RTL
                LineAlignment = StringAlignment.Center,
                Trimming      = StringTrimming.EllipsisPath,
                FormatFlags   = StringFormatFlags.NoWrap | StringFormatFlags.DirectionRightToLeft,
            };

            e.Graphics.DrawString(
                e.SubItem.Text,
                FontUi,
                selected ? SystemBrushes.HighlightText : new SolidBrush(ColorTextPrimary),
                textBounds,
                format);

            // Thin row separator
            using (var pen = new Pen(ColorBorder, 1))
                e.Graphics.DrawLine(pen,
                    e.Bounds.Left,  e.Bounds.Bottom - 1,
                    e.Bounds.Right, e.Bounds.Bottom - 1);
        }

        // ── Button factory ────────────────────────────────────────────────────────

        private static Button MakeButton(string text, Color background, Color foreground, Color hoverBackground)
        {
            var btn = new Button
            {
                Text      = text,
                Font      = FontUi,
                ForeColor = foreground,
                BackColor = background,
                FlatStyle = FlatStyle.Flat,
                Cursor    = Cursors.Hand,
            };
            btn.FlatAppearance.BorderSize  = 1;
            btn.FlatAppearance.BorderColor = ColorBorder;
            btn.FlatAppearance.MouseOverBackColor = hoverBackground;
            btn.FlatAppearance.MouseDownBackColor = hoverBackground;
            return btn;
        }

        // ── Event handlers ────────────────────────────────────────────────────────

        private void OnSelectionChanged(object sender, EventArgs e)
        {
            _deleteButton.Enabled = _listView.SelectedItems.Count > 0;
        }

        private void OnAddClicked(object sender, EventArgs e)
        {
            using (var dialog = new FolderBrowserDialog())
            {
                dialog.Description         = "בחר תיקייה להחרגה מחיפוש הקבצים";
                dialog.ShowNewFolderButton = false;
                dialog.RootFolder          = Environment.SpecialFolder.MyComputer;

                if (dialog.ShowDialog(this) != DialogResult.OK) return;

                string selected = dialog.SelectedPath;
                if (string.IsNullOrEmpty(selected)) return;

                // Deduplicate
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
            // Sync _folders with the current ListView contents on OK.
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
            {
                FontUi?.Dispose();
                FontSmall?.Dispose();
                FontTitle?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
