using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace FluentChromeTabs
{
    public enum FluentChromeTabsTheme { Light, Dark }

    public class FluentTab
    {
        public object Tag { get; set; }
        public string Title { get; set; }
        public string ListTitle { get; set; }
        public Image Icon { get; set; }
        public int Group { get; set; }
        public bool Highlighted { get; set; }
    }

    public class FluentTabEventArgs : EventArgs { public FluentTab Tab { get; set; } }
    public class FluentTabClosingEventArgs : EventArgs { public FluentTab Tab { get; set; } public bool Cancel { get; set; } }
    public class NewTabRequestedEventArgs : EventArgs { public bool Cancel { get; set; } public int Group { get; set; } }
    public class FluentTabGroupEventArgs : EventArgs { public FluentTab Tab { get; set; } public int Group { get; set; } }

    public class FluentChromeTabsForm : Form
    {
        public bool AllowTabDetach { get; set; }
        public bool ExitOnLastTabClose { get; set; }
        public string TabListOpenTabsHeader { get; set; }
        public string MinimizeToolTip { get; set; }
        public string MaximizeToolTip { get; set; }
        public string RestoreToolTip { get; set; }
        public string CloseToolTip { get; set; }
        public string CloseTabMenuText { get; set; }
        public string CloseOtherTabsMenuText { get; set; }
        public string CloseTabsToTheLeftMenuText { get; set; }
        public string CloseTabsToTheRightMenuText { get; set; }

        public int StripHeight { get; set; }
        public int TabHeight { get; set; }
        public bool ShowTabListButton { get; set; }
        public bool StripVisible { get; set; }
        public int SelectedTabGroup { get; set; }
        public bool SplitStrip { get; set; }
        public double SplitRatio { get; set; }
        public Color? AccentColor { get; set; }
        public Color? SplitDividerColor { get; set; }
        public Color CustomThemeColor { get; set; }
        public FluentChromeTabsTheme Theme { get; set; }

        private readonly List<FluentTab> _tabs = new List<FluentTab>();
        public IEnumerable<FluentTab> Tabs => _tabs;
        public FluentTab SelectedTab { get; set; }

        public event EventHandler<FluentTabEventArgs> SelectedTabChanged;
        public event EventHandler<FluentTabClosingEventArgs> TabClosing;
        public event EventHandler<NewTabRequestedEventArgs> NewTabRequested;
        public event EventHandler SplitRatioChanged;
        public event EventHandler<FluentTabGroupEventArgs> TabDraggedToGroup;

        public FluentTab AddTab(string title)
        {
            var tab = new FluentTab { Title = title };
            _tabs.Add(tab);
            return tab;
        }

        public void CloseTab(FluentTab tab) => _tabs.Remove(tab);
        public void ShowTabListMenu(int group, int quickSwitchStep) { }
        public void SetSplitDividerPixels(int left, int width) { }

        protected void RaiseUnused()
        {
            SelectedTabChanged?.Invoke(this, new FluentTabEventArgs());
            TabClosing?.Invoke(this, new FluentTabClosingEventArgs());
            NewTabRequested?.Invoke(this, new NewTabRequestedEventArgs());
            SplitRatioChanged?.Invoke(this, EventArgs.Empty);
            TabDraggedToGroup?.Invoke(this, new FluentTabGroupEventArgs());
        }
    }
}
