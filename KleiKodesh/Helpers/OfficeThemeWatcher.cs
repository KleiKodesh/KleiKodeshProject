using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Management;
using System.Security.Principal;
using System.Windows.Forms;

public static class OfficeThemeWatcher
{
    /* ================= STATE ================= */
    private static readonly HashSet<Control> _roots = new HashSet<Control>();
    private static ManagementEventWatcher _watcher;
    private static bool _watcherRunning;

    private static readonly OfficeTheme _theme = new OfficeTheme();

    /* ================= PUBLIC API ================= */
    public static void Attach(Control root)
    {
        if (root == null || root.IsDisposed)
            return;

        bool startWatcher = false;

        lock (_roots)
        {
            if (_roots.Add(root))
            {
                root.ControlAdded += OnControlAdded;
                root.Disposed += OnRootDisposed;

                if (_roots.Count == 1)
                    startWatcher = true;
            }
        }

        if (startWatcher)
            EnsureWatcher();

        AttachRecursive(root);
    }

    /* ================= CONTROL LIFECYCLE ================= */
    private static void OnRootDisposed(object sender, EventArgs e)
    {
        var root = sender as Control;
        if (root == null) return;

        bool stopWatcher = false;

        lock (_roots)
        {
            root.ControlAdded -= OnControlAdded;
            root.Disposed -= OnRootDisposed;
            _roots.Remove(root);

            if (_roots.Count == 0)
                stopWatcher = true;
        }

        if (stopWatcher)
            StopWatcher();
    }

    private static void OnControlAdded(object sender, ControlEventArgs e)
    {
        AttachRecursive(e.Control);
    }

    private static void AttachRecursive(Control control)
    {
        AttachControl(control);
        foreach (Control child in control.Controls)
            AttachRecursive(child);
    }

    private static void AttachControl(Control control)
    {
        // Bind BackColor / ForeColor for all controls
        if (control.DataBindings.Count == 0)
        {
            control.DataBindings.Add(nameof(Control.BackColor), _theme, nameof(OfficeTheme.BackColor), false, DataSourceUpdateMode.Never);
            control.DataBindings.Add(nameof(Control.ForeColor), _theme, nameof(OfficeTheme.ForeColor), false, DataSourceUpdateMode.Never);
        }

        // Special button styling
        if (control is Button btn)
            AttachButtonTheme(btn);
    }

    private static void AttachButtonTheme(Button btn)
    {
        btn.UseVisualStyleBackColor = false;
        btn.FlatStyle = FlatStyle.Flat;

        void Apply()
        {
            btn.FlatAppearance.BorderSize = 1;
            btn.FlatAppearance.BorderColor = _theme.ButtonBorder;
            btn.FlatAppearance.MouseOverBackColor = _theme.ButtonHover;
            btn.FlatAppearance.MouseDownBackColor = _theme.ButtonPressed;
        }

        Apply();

        PropertyChangedEventHandler handler = (s, e) =>
        {
            if (e.PropertyName == nameof(OfficeTheme.ButtonBorder) ||
                e.PropertyName == nameof(OfficeTheme.ButtonHover) ||
                e.PropertyName == nameof(OfficeTheme.ButtonPressed))
            {
                Apply();
            }
        };

        _theme.PropertyChanged += handler;

        btn.Disposed += (s, e) =>
        {
            _theme.PropertyChanged -= handler;
        };
    }

    /* ================= WATCHER ================= */
    private static void EnsureWatcher()
    {
        if (_watcherRunning)
            return;

        UpdateThemeSafe(ReadCurrentTheme());

        string version = GetOfficeVersionKey();
        if (version == null) return;

        string sid = WindowsIdentity.GetCurrent().User?.Value;
        if (string.IsNullOrEmpty(sid)) return;

        string query =
            "SELECT * FROM RegistryValueChangeEvent " +
            "WHERE Hive='HKEY_USERS' " +
            "AND KeyPath='" + sid + "\\\\Software\\\\Microsoft\\\\Office\\\\" + version + "\\\\Common' " +
            "AND ValueName='UI Theme'";

        _watcher = new ManagementEventWatcher(new WqlEventQuery(query));
        _watcher.EventArrived += (s, e) =>
        {
            UpdateThemeSafe(ReadCurrentTheme());
        };
        _watcher.Start();

        _watcherRunning = true;
    }

    private static void StopWatcher()
    {
        if (!_watcherRunning)
            return;

        try
        {
            _watcher.Stop();
            _watcher.Dispose();
        }
        catch { }

        _watcher = null;
        _watcherRunning = false;
    }

    /* ================= THEME UPDATE ================= */
    private static void UpdateThemeSafe(OfficeTheme newTheme)
    {
        foreach (var root in _roots)
        {
            if (root.IsHandleCreated)
            {
                root.BeginInvoke((Action)(() =>
                {
                    UpdateTheme(newTheme);
                }));
                break;
            }
        }
    }

    private static void UpdateTheme(OfficeTheme newTheme)
    {
        _theme.BackColor = newTheme.BackColor;
        _theme.ForeColor = newTheme.ForeColor;
        _theme.ButtonHover = newTheme.ButtonHover;
        _theme.ButtonPressed = newTheme.ButtonPressed;
        _theme.ButtonBorder = newTheme.ButtonBorder;
    }

    /* ================= THEME READ ================= */
    private static OfficeTheme ReadCurrentTheme()
    {
        switch (ReadThemeCode())
        {
            case OfficeThemeCode.Black:
                return new OfficeTheme(
                    Color.FromArgb(38, 38, 38),
                    Color.White,
                    Color.FromArgb(70, 70, 70),
                    Color.FromArgb(90, 90, 90),
                    Color.FromArgb(120, 120, 120));

            case OfficeThemeCode.DarkGray:
                return new OfficeTheme(
                    Color.FromArgb(102, 102, 102),
                    Color.White,
                    Color.FromArgb(160, 160, 160),
                    Color.FromArgb(180, 180, 180),
                    Color.FromArgb(200, 200, 200));

            default:
                return new OfficeTheme(
                    Color.White,
                    Color.FromArgb(38, 38, 38),
                    Color.FromArgb(229, 241, 251),
                    Color.FromArgb(204, 228, 247),
                    Color.FromArgb(200, 200, 200));
        }
    }

    private static OfficeThemeCode ReadThemeCode()
    {
        string version = GetOfficeVersionKey();
        if (version == null) return OfficeThemeCode.Colorful;

        using (var key = Registry.CurrentUser.OpenSubKey(
            @"Software\Microsoft\Office\" + version + @"\Common"))
        {
            object value = key?.GetValue("UI Theme");
            return value is int ? (OfficeThemeCode)value : OfficeThemeCode.Colorful;
        }
    }

    private static string GetOfficeVersionKey()
    {
        foreach (string v in new[] { "16.0", "15.0", "14.0" })
        {
            using (var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Office\" + v + @"\Common"))
            {
                if (key != null) return v;
            }
        }
        return null;
    }

    /* ================= SUPPORT TYPES ================= */
    private enum OfficeThemeCode { Colorful = 0, DarkGray = 3, Black = 4, White = 5 }

    private sealed class OfficeTheme : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        private Color _back, _fore, _hover, _pressed, _border;

        public Color BackColor { get => _back; set { _back = value; OnChanged(nameof(BackColor)); } }
        public Color ForeColor { get => _fore; set { _fore = value; OnChanged(nameof(ForeColor)); } }
        public Color ButtonHover { get => _hover; set { _hover = value; OnChanged(nameof(ButtonHover)); } }
        public Color ButtonPressed { get => _pressed; set { _pressed = value; OnChanged(nameof(ButtonPressed)); } }
        public Color ButtonBorder { get => _border; set { _border = value; OnChanged(nameof(ButtonBorder)); } }

        public OfficeTheme() { }

        public OfficeTheme(Color back, Color fore, Color hover, Color pressed, Color border)
        {
            _back = back;
            _fore = fore;
            _hover = hover;
            _pressed = pressed;
            _border = border;
        }

        private void OnChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
