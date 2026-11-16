using KleiKodesh.Helpers;
using KleiKodesh.Ribbon;
using Nakdan.UI;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Wpf = System.Windows.Controls;

namespace KleiKodesh.ToolRail
{
    internal enum ToolKind
    {
        // Lives in the shared pane's content area, built once and kept alive.
        Pane,
        // Starts or focuses a desktop program and snaps it beside Word.
        Program,
        // Runs a Word macro.
        Macro,
    }

    /// <summary>
    /// One item on the rail: a built-in tool, or an external item from tool-rail.json.
    /// A Pane item has exactly one of CreateControl (WinForms) or CreateWpfView (WPF).
    /// </summary>
    internal sealed class ToolEntry
    {
        public string Id;
        public ToolKind Kind;
        public Func<string> Label;
        public Func<string> Tip;
        public int DefaultWidth = 520;
        public Func<UserControl> CreateControl;
        public Func<Wpf.UserControl> CreateWpfView;
        public Func<Image> LoadIcon;
        public bool External;

        // External items only.
        public string Path;
        public string Arguments;
        public string Url;
        public string Macro;
        public bool Snap = true;

        /// <summary>
        /// Builds the WinForms control that goes into a slot. WPF views come back
        /// wrapped in their own WpfHostControl, with the view itself in
        /// <paramref name="wpfView"/> so the caller can hand it a pop-out toggle.
        /// </summary>
        public Control BuildSlotControl(out Wpf.UserControl wpfView)
        {
            wpfView = null;
            if (CreateWpfView != null)
            {
                wpfView = CreateWpfView();
                return WpfTaskPane.BuildHost(wpfView);
            }
            return CreateControl?.Invoke();
        }
    }

    /// <summary>
    /// Every tool the shared pane can open: the add-in's own, then the user's external
    /// items. Ids are the ribbon control ids, so AddinCommands.Run, VBA and the rail all
    /// name a tool the same way. The rail itself lists only the tools the user has
    /// opened (ToolRailHost), never this whole catalog.
    /// </summary>
    internal static class ToolCatalog
    {
        static readonly List<ToolEntry> BuiltIn = new List<ToolEntry>
        {
            new ToolEntry
            {
                Id = "KitveiHakodesh", DefaultWidth = 610,
                CreateControl = () => new KitveiHakodeshLib.AppViewer { ShowPopOutButton = true },
            },
            new ToolEntry
            {
                Id = "Kiwix", DefaultWidth = 610,
                CreateControl = () => new KiwixLib.KiwixWebview(),
            },
            new ToolEntry
            {
                Id = "WebSites", DefaultWidth = 510,
                CreateWpfView = () => new WebSitesLib.UI.WebSitesView(),
            },
            new ToolEntry
            {
                Id = "DocDesign", DefaultWidth = 520,
                CreateWpfView = () => new DocDesign.DocDesignView(Globals.ThisAddIn.Application, Globals.Factory),
            },
            new ToolEntry
            {
                Id = "Nakdan", DefaultWidth = 520,
                CreateWpfView = () => new NakdanView(Globals.ThisAddIn.Application, Globals.Factory),
            },
            new ToolEntry
            {
                Id = "RegexFind", DefaultWidth = 600,
                CreateWpfView = () => new RegexFindLib.UI.RegexFindView(Globals.ThisAddIn.Application, Globals.Factory),
            },
            new ToolEntry
            {
                Id = "Settings", DefaultWidth = 400,
                CreateWpfView = () => new RibbonSettingsView(AddinCommands.Ribbon),
            },
        };

        static ToolCatalog()
        {
            foreach (var e in BuiltIn)
            {
                var id = e.Id;
                e.Kind = ToolKind.Pane;
                e.Label = () => AddinText.Label(id);
                e.Tip = () => AddinText.Tip(id);
                e.LoadIcon = () => LoadImageFile(System.IO.Path.Combine(
                    AppDomain.CurrentDomain.BaseDirectory, "Resources", id + ".png"));
            }
        }

        /// <summary>Whether <paramref name="id"/> is a built-in tool the rail can show.</summary>
        public static bool IsBuiltInTool(string id) => BuiltIn.Any(e => e.Id == id);

        /// <summary>The item with this id, built-in or external, or null.</summary>
        public static ToolEntry Find(string id) =>
            BuiltIn.FirstOrDefault(e => e.Id == id)
            ?? ExternalTools.Load().FirstOrDefault(e => e.Id == id);

        /// <summary>
        /// Loads an image without keeping the file locked (new Bitmap(path) holds the
        /// file open for the bitmap's lifetime). Null when missing or unreadable.
        /// </summary>
        public static Image LoadImageFile(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;

                if (path.EndsWith(".ico", StringComparison.OrdinalIgnoreCase))
                    using (var icon = new Icon(path, 64, 64))
                        return icon.ToBitmap();

                using (var stream = new MemoryStream(File.ReadAllBytes(path)))
                using (var image = Image.FromStream(stream))
                    return new Bitmap(image);
            }
            catch { return null; }
        }
    }
}
