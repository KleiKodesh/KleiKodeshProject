using System;
using System.IO;
using System.Linq;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Capturing;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.UIA3;

namespace WpfLib.Gallery.Tests
{
    /// <summary>
    /// Launches the gallery once and drives it through UIAutomation.
    ///
    /// Everything here is addressed by name or control type, never by screen
    /// position. That is the whole reason for using FlaUI rather than clicking
    /// at coordinates: an element capture photographs the element, so it cannot
    /// accidentally photograph whatever window is on top.
    ///
    /// The window is kept ON SCREEN and in the foreground for the duration of
    /// the run, and that is deliberate rather than lazy.
    ///
    /// FlaUI's Capture.Element grabs the screen inside the element's bounding
    /// rectangle; it does not render the window off-screen. Parking the window
    /// at -4000 therefore produced 36 photographs of the desktop, one of which
    /// caught the Task View overlay and the other windows open behind it.
    ///
    /// So a run takes over the screen for about a minute. That is normal for
    /// desktop UI automation. If that ever becomes unacceptable, the fix is a
    /// RenderTargetBitmap hook inside the gallery, not a cleverer screen grab.
    /// </summary>
    public sealed class GalleryDriver : IDisposable
    {
        private readonly Application _app;
        private readonly UIA3Automation _automation;

        public Window Window { get; }

        public GalleryDriver()
        {
            _app = Application.Launch(GalleryExePath());
            _automation = new UIA3Automation();
            Window = _app.GetMainWindow(_automation, TimeSpan.FromSeconds(20));

            // A fixed position and size keep the captures comparable between runs.
            Window.Move(0, 0);
            Window.Patterns.Transform.Pattern.Resize(900, 620);
            Window.SetForeground();
            WaitIdle();
        }

        /// <summary>
        /// The gallery is built for net48 and sits beside this project's output.
        /// Walking up to the repo root keeps this working from any bin folder.
        /// </summary>
        internal static string GalleryExePath()
        {
            DirectoryInfo? dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "WpfLib.Gallery")))
                dir = dir.Parent;

            if (dir == null)
                throw new InvalidOperationException("Could not locate the WpfLib.Gallery project from " + AppContext.BaseDirectory);

            var exe = Path.Combine(dir.FullName, "WpfLib.Gallery", "bin", "Debug", "net48", "WpfLib.Gallery.exe");
            if (!File.Exists(exe))
                throw new FileNotFoundException("Build WpfLib.Gallery first; expected " + exe);

            return exe;
        }

        /// <summary>Pick a section from the rail on the left, by its label.</summary>
        public void ShowSection(string label)
        {
            var item = Window.FindFirstDescendant(cf => cf.ByName(label))
                       ?? throw new InvalidOperationException($"No rail item named '{label}'.");
            item.AsListBoxItem().Select();
            WaitIdle();
        }

        /// <summary>Switch the window to one of the four Office themes, by name.</summary>
        public void ShowTheme(string themeName)
        {
            ThemePicker().Select(themeName);
            WaitIdle();
        }

        /// <summary>The header's theme picker. Always the first combo in the window.</summary>
        public ComboBox ThemePicker() =>
            Window.FindAllDescendants(cf => cf.ByControlType(ControlType.ComboBox))
                  .First()
                  .AsComboBox();

        /// <summary>The combo boxes inside the section body, i.e. excluding the theme picker.</summary>
        public ComboBox[] SectionComboBoxes() =>
            Window.FindAllDescendants(cf => cf.ByControlType(ControlType.ComboBox))
                  .Skip(1)
                  .Select(e => e.AsComboBox())
                  .ToArray();

        /// <summary>Capture the whole window to a PNG and hand back the path.</summary>
        public string CaptureWindow(string name)
        {
            Window.SetForeground();
            WaitIdle();

            var path = Path.Combine(Path.GetTempPath(), "wpflib-gallery-" + name + ".png");
            Capture.Element(Window).ToFile(path);
            return path;
        }

        public void WaitIdle()
        {
            Wait.UntilInputIsProcessed();
            System.Threading.Thread.Sleep(450);   // let WPF finish the render pass
        }

        public void Dispose()
        {
            try { _app.Close(); } catch { /* the window may already be gone */ }
            try { _app.Dispose(); } catch { }
            _automation.Dispose();
        }
    }
}
