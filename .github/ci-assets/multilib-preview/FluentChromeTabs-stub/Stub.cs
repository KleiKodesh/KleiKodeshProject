using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

// Behaviour-free COMPILE stub. Its public surface mirrors the released FluentChromeTabs (same member
// names, types and nullability) for everything the repo uses: code compiled against this stub calls
// members by exact signature, and the add-in's REAL FluentChromeTabs.dll must have them (a member
// that differs here fails at run time with MissingMethodException, e.g. Color vs Color?).
namespace FluentChromeTabs
{
    public enum FluentChromeTabsTheme { Light, Dark }

    public class FluentTab
    {
        public FluentTab(string title) { Title = title; }
        public FluentTab(string title, Control content) { Title = title; Content = content; }
        public FluentChromeTabsForm Owner { get; set; }
        public Control Content { get; private set; }
        public string Title { get; set; }
        public string ListTitle { get; set; }
        public Image Icon { get; set; }
        public bool CanClose { get; set; }
        public int Group { get; set; }
        public bool Highlighted { get; set; }
        public object Tag { get; set; }
    }

    public class FluentTabEventArgs : EventArgs
    {
        public FluentTabEventArgs(FluentTab tab) { Tab = tab; }
        public FluentTab Tab { get; private set; }
    }
    public class FluentTabClosingEventArgs : FluentTabEventArgs
    {
        public FluentTabClosingEventArgs(FluentTab tab) : base(tab) { }
        public bool Cancel { get; set; }
    }
    public class NewTabRequestedEventArgs : EventArgs
    {
        public string Title { get; set; }
        public Control Content { get; set; }
        public bool Cancel { get; set; }
        public int Group { get; set; }
    }
    public class FluentTabGroupEventArgs : EventArgs
    {
        public FluentTabGroupEventArgs(FluentTab tab, int group) { Tab = tab; Group = group; }
        public FluentTab Tab { get; private set; }
        public int Group { get; private set; }
    }

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
        public int SelectedTabGroup { get; private set; }
        public bool SplitStrip { get; set; }
        public double SplitRatio { get; set; }
        public Color? AccentColor { get; set; }
        public Color? SplitDividerColor { get; set; }
        public Color? CustomThemeColor { get; set; }
        public FluentChromeTabsTheme Theme { get; set; }

        private readonly List<FluentTab> _tabs = new List<FluentTab>();
        public IReadOnlyList<FluentTab> Tabs => _tabs;
        public FluentTab SelectedTab { get; set; }

#pragma warning disable CS0067
        public event EventHandler<FluentTabEventArgs> SelectedTabChanged;
        public event EventHandler<FluentTabClosingEventArgs> TabClosing;
        public event EventHandler<NewTabRequestedEventArgs> NewTabRequested;
        public event EventHandler SplitRatioChanged;
        public event EventHandler<FluentTabGroupEventArgs> TabDraggedToGroup;
#pragma warning restore CS0067

        public FluentTab AddTab(string title)
        {
            var tab = new FluentTab(title);
            _tabs.Add(tab);
            return tab;
        }

        public bool CloseTab(FluentTab tab) => _tabs.Remove(tab);
        public void ShowTabListMenu(int group, int quickSwitchStep) { }
        public void SetSplitDividerPixels(int leftPx, int widthPx) { }
    }
}
