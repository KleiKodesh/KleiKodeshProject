using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Management;
using System.Security.Principal;
using System.Windows.Forms;

public static class OfficeThemeWatcher
{
    /* ================= STATE ================= */

    private static readonly HashSet<Control> _roots = new HashSet<Control>();
    private static ManagementEventWatcher _watcher;
    private static OfficeTheme _currentTheme;
    private static bool _watcherRunning;

    /* ================= PUBLIC API ================= */

    public static void Attach(Control control)
    {
        if (control == null || control.IsDisposed)
            return;

        bool startWatcher = false;

        lock (_roots)
        {
            if (_roots.Add(control))
            {
                control.ControlAdded += ControlAdded;
                control.Disposed += ControlDisposed;

                if (_roots.Count == 1)
                    startWatcher = true;
            }
        }

        if (startWatcher)
            EnsureWatcher();

        ApplyThemeRecursive(control);
    }

    /* ================= CONTROL LIFECYCLE ================= */

    private static void ControlDisposed(object sender, EventArgs e)
    {
        var control = sender as Control;
        if (control == null)
            return;

        bool stopWatcher = false;

        lock (_roots)
        {
            control.ControlAdded -= ControlAdded;
            control.Disposed -= ControlDisposed;

            _roots.Remove(control);

            if (_roots.Count == 0)
                stopWatcher = true;
        }

        if (stopWatcher)
            StopWatcher();
    }

    private static void ControlAdded(object sender, ControlEventArgs e)
    {
        ApplyThemeRecursive(e.Control);
    }

    /* ================= THEME APPLY ================= */

    private static void ApplyThemeRecursive(Control root)
    {
        ApplyTheme(root);

        foreach (Control child in root.Controls)
            ApplyThemeRecursive(child);
    }

    private static void ApplyTheme(Control control)
    {
        control.BackColor = _currentTheme.BackColor;
        control.ForeColor = _currentTheme.ForeColor;

        var btn = control as Button;
        if (btn != null)
            ApplyFlatButtonTheme(btn);
    }

    private static void ApplyFlatButtonTheme(Button btn)
    {
        btn.UseVisualStyleBackColor = false;
        btn.FlatStyle = FlatStyle.Flat;

        btn.FlatAppearance.BorderColor = _currentTheme.ButtonBorder;
        btn.FlatAppearance.BorderSize = 1;
        btn.FlatAppearance.MouseOverBackColor = _currentTheme.ButtonHover;
        btn.FlatAppearance.MouseDownBackColor = _currentTheme.ButtonPressed;
    }

    /* ================= WATCHER ================= */

    private static void EnsureWatcher()
    {
        if (_watcherRunning)
            return;

        _currentTheme = ReadCurrentTheme();

        string version = GetOfficeVersionKey();
        if (version == null)
            return;

        string sid = WindowsIdentity.GetCurrent().User != null
            ? WindowsIdentity.GetCurrent().User.Value
            : null;

        if (string.IsNullOrEmpty(sid))
            return;

        string query =
            "SELECT * FROM RegistryValueChangeEvent " +
            "WHERE Hive='HKEY_USERS' " +
            "AND KeyPath='" + sid + "\\\\Software\\\\Microsoft\\\\Office\\\\" + version + "\\\\Common' " +
            "AND ValueName='UI Theme'";

        _watcher = new ManagementEventWatcher(new WqlEventQuery(query));
        _watcher.EventArrived += (s, e) => OnThemeChanged();
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

    private static void OnThemeChanged()
    {
        _currentTheme = ReadCurrentTheme();

        lock (_roots)
        {
            foreach (var root in _roots)
            {
                if (root.IsDisposed || !root.IsHandleCreated)
                    continue;

                root.BeginInvoke((Action)(() =>
                {
                    ApplyThemeRecursive(root);
                }));
            }
        }
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
                    Color.FromArgb(90, 90, 90),
                    Color.FromArgb(120, 120, 120),
                    Color.FromArgb(150, 150, 150));

            case OfficeThemeCode.DarkGray:
                return new OfficeTheme(
                    Color.FromArgb(102, 102, 102),
                    Color.White,
                    Color.FromArgb(200, 200, 200),
                    Color.FromArgb(180, 180, 180),
                    Color.FromArgb(190, 190, 190));

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
        if (version == null)
            return OfficeThemeCode.Colorful;

        using (var key = Registry.CurrentUser.OpenSubKey(
            @"Software\Microsoft\Office\" + version + @"\Common"))
        {
            object value = key != null ? key.GetValue("UI Theme") : null;
            return value is int
                ? (OfficeThemeCode)value
                : OfficeThemeCode.Colorful;
        }
    }

    private static string GetOfficeVersionKey()
    {
        foreach (string v in new[] { "16.0", "15.0", "14.0" })
        {
            using (var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Office\" + v + @"\Common"))
            {
                if (key != null)
                    return v;
            }
        }
        return null;
    }

    /* ================= SUPPORT TYPES ================= */

    private enum OfficeThemeCode
    {
        Colorful = 0,
        DarkGray = 3,
        Black = 4,
        White = 5
    }

    private sealed class OfficeTheme
    {
        public Color BackColor { get; private set; }
        public Color ForeColor { get; private set; }
        public Color ButtonHover { get; private set; }
        public Color ButtonPressed { get; private set; }
        public Color ButtonBorder { get; private set; }

        public OfficeTheme(
            Color back,
            Color fore,
            Color hover,
            Color pressed,
            Color border)
        {
            BackColor = back;
            ForeColor = fore;
            ButtonHover = hover;
            ButtonPressed = pressed;
            ButtonBorder = border;
        }
    }
}
