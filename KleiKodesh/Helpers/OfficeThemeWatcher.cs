using Microsoft.Win32;
using System;
using System.Drawing;
using System.Management;
using System.Security.Principal;
using System.Windows.Forms;

public sealed class OfficeThemeWatcher : IDisposable
{
    /* ================= STATE ================= */

    private ManagementEventWatcher _watcher;
    private OfficeTheme _currentTheme;
    private Control _root;

    /* ================= PUBLIC API ================= */

    public OfficeThemeWatcher()
    {
        _currentTheme = ReadCurrentTheme();
        StartWatcher();
    }

    public void Attach(Control control)
    {
        if (control == null)
            return;

        _root = control;

        RegisterControlTree(control);

        control.ControlAdded -= ControlAdded;
        control.ControlAdded += ControlAdded;
    }

    public void Dispose()
    {
        if (_watcher != null)
        {
            _watcher.Stop();
            _watcher.Dispose();
            _watcher = null;
        }
    }

    /* ================= CONTROL TREE ================= */

    private void RegisterControlTree(Control root)
    {
        ApplyTheme(root);

        foreach (Control child in root.Controls)
            RegisterControlTree(child);
    }

    private void ControlAdded(object sender, ControlEventArgs e)
    {
        RegisterControlTree(e.Control);
    }

    /* ================= THEME APPLY ================= */

    private void ApplyTheme(Control control)
    {
        control.BackColor = _currentTheme.BackColor;
        control.ForeColor = _currentTheme.ForeColor;

        if (control is Button btn)
            ApplyFlatButtonTheme(btn);
    }

    private void ApplyFlatButtonTheme(Button btn)
    {
        btn.UseVisualStyleBackColor = false;
        btn.FlatStyle = FlatStyle.Flat;

        //btn.BackColor = _currentTheme.ButtonBack;
        //btn.ForeColor = _currentTheme.ForeColor;

        btn.FlatAppearance.BorderColor = _currentTheme.ButtonBorder;
        btn.FlatAppearance.BorderSize = 1;

        // Fluent-style soft colors
        btn.FlatAppearance.MouseOverBackColor = _currentTheme.ButtonHover;
        btn.FlatAppearance.MouseDownBackColor = _currentTheme.ButtonPressed;
    }


    /* ================= THEME CHANGE ================= */

    private void OnThemeChanged()
    {
        if (_root == null || _root.IsDisposed || !_root.IsHandleCreated)
            return;

        _currentTheme = ReadCurrentTheme();

        _root.BeginInvoke((Action)(() =>
        {
            RegisterControlTree(_root);
        }));
    }

    /* ================= THEME READ ================= */

    private OfficeTheme ReadCurrentTheme()
    {
        OfficeThemeCode code = ReadThemeCode();

        switch (code)
        {
            case OfficeThemeCode.Black:
                return new OfficeTheme(
                    Color.FromArgb(38, 38, 38),      // BackColor
                    Color.White,                     // ForeColor
                    Color.FromArgb(60, 60, 60),      // ButtonBack
                    Color.FromArgb(90, 90, 90),      // ButtonHover (soft Fluent gray)
                    Color.FromArgb(120, 120, 120),   // ButtonPressed
                    Color.FromArgb(150, 150, 150));  // ButtonBorder

            case OfficeThemeCode.DarkGray:
                return new OfficeTheme(
                    Color.FromArgb(102, 102, 102),
                    Color.White,
                    Color.FromArgb(160, 160, 160),   // ButtonBack
                    Color.FromArgb(200, 200, 200),   // ButtonHover (Fluent soft hover)
                    Color.FromArgb(180, 180, 180),   // ButtonPressed
                    Color.FromArgb(190, 190, 190));  // ButtonBorder

            case OfficeThemeCode.White:
            default:
                return new OfficeTheme(
                    Color.White,
                    Color.FromArgb(38, 38, 38),
                    Color.FromArgb(240, 240, 240),   // ButtonBack
                    Color.FromArgb(229, 241, 251),   // ButtonHover (Fluent blue-ish hover)
                    Color.FromArgb(204, 228, 247),   // ButtonPressed
                    Color.FromArgb(200, 200, 200));  // ButtonBorder
        }
    }


    private OfficeThemeCode ReadThemeCode()
    {
        string version = GetOfficeVersionKey();
        if (version == null)
            return OfficeThemeCode.Colorful;

        using (RegistryKey key = Registry.CurrentUser.OpenSubKey(
            @"Software\Microsoft\Office\" + version + @"\Common"))
        {
            if (key?.GetValue("UI Theme") is int value)
                return (OfficeThemeCode)value;
        }

        return OfficeThemeCode.Colorful;
    }

    /* ================= WATCHER ================= */

    private void StartWatcher()
    {
        string version = GetOfficeVersionKey();
        if (version == null)
            return;

        string sid = WindowsIdentity.GetCurrent().User?.Value;
        if (string.IsNullOrEmpty(sid))
            return;

        string query =
            "SELECT * FROM RegistryValueChangeEvent " +
            "WHERE Hive='HKEY_USERS' " +
            $"AND KeyPath='{sid}\\\\Software\\\\Microsoft\\\\Office\\\\{version}\\\\Common' " +
            "AND ValueName='UI Theme'";

        _watcher = new ManagementEventWatcher(new WqlEventQuery(query));
        _watcher.EventArrived += (_, __) => OnThemeChanged();
        _watcher.Start();
    }

    /* ================= OFFICE VERSION ================= */

    private static string GetOfficeVersionKey()
    {
        string[] versions = { "16.0", "15.0", "14.0" };

        foreach (string v in versions)
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(
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
        public Color BackColor { get; }
        public Color ForeColor { get; }
        public Color ButtonBack { get; }
        public Color ButtonHover { get; }
        public Color ButtonPressed { get; }
        public Color ButtonBorder { get; }

        public OfficeTheme(
            Color back,
            Color fore,
            Color btnBack,
            Color btnHover,
            Color btnPressed,
            Color btnBorder)
        {
            BackColor = back;
            ForeColor = fore;
            ButtonBack = btnBack;
            ButtonHover = btnHover;
            ButtonPressed = btnPressed;
            ButtonBorder = btnBorder;
        }
    }
}
