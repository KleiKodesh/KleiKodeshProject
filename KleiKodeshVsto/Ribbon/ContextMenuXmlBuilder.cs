using System.Collections.Generic;
using System.Text;

namespace KleiKodesh.Ribbon
{
    /// <summary>
    /// Builds the &lt;contextMenus&gt; half of the ribbon customUI at run time.
    ///
    /// Word swaps in a DIFFERENT context menu depending on what sits under the
    /// cursor, and Office has no "all menus" hook, so the same four items must be
    /// repeated once per menu with globally-unique control ids. Written out by hand
    /// that is ~90 near-identical XML elements; here it is one row per menu in
    /// Menus, plus a single copy of the item definitions.
    ///
    /// The per-menu suffix does not change behaviour: contextMenu_Click keys off the
    /// substring "Catalog" in the id, and the other handlers ignore the id entirely.
    ///
    /// No insertBeforeMso/insertAfterMso anchors: an anchor naming a control that is
    /// not present on a given menu (or in a given Word build) makes Office drop the
    /// item SILENTLY. Unanchored items append to the end of the menu - less
    /// prominent, but they always show up.
    /// </summary>
    internal static class ContextMenuXmlBuilder
    {
        // Word's context-menu idMso -> the suffix that keeps this menu's control ids
        // unique. A duplicate id anywhere makes Office reject the WHOLE customUI, so
        // every suffix here must stay distinct.
        //
        // BEFORE ADDING A ROW: verify the idMso against Microsoft's wordcontrols.xlsx
        // (github.com/OfficeDev/office-fluent-ui-command-identifiers). An idMso that
        // Word does not know is NOT ignored - it fails customUI validation and takes
        // down the entire ribbon, this add-in's tab included, with no visible error.
        // Plausible-looking names are the trap: ContextMenuTableHeading and
        // ContextMenuTrackChanges both sound right and neither exists (the real ones
        // are ContextMenuHeadingTable and ContextMenuRevision).
        //
        // ContextMenuText deliberately keeps the empty suffix: those were the
        // original control ids, and renaming them would break anything keyed to them.
        private static readonly KeyValuePair<string, string>[] Menus =
        {
            Menu("ContextMenuText",         ""),        // plain body text
            Menu("ContextMenuList",         "Lst"),     // numbered / bulleted paragraph
            Menu("ContextMenuListTable",    "LstTbl"),  // list paragraph inside a table
            Menu("ContextMenuSpell",        "Sp"),      // word flagged as a misspelling
            Menu("ContextMenuGrammar",      "Gr"),      // grammar / style squiggle
            Menu("ContextMenuFootnote",     "Fn"),      // text inside a footnote
            Menu("ContextMenuEndnote",      "En"),      // text inside an endnote
            Menu("ContextMenuTextTable",    "TxtTbl"),  // plain text in a table cell
            Menu("ContextMenuTableCell",    "TblCell"), // cell selection
            Menu("ContextMenuTable",        "Tbl"),     // table selection
            Menu("ContextMenuTableWhole",   "TblAll"),  // whole-table selection
            Menu("ContextMenuHeading",      "Head"),    // heading paragraph
            Menu("ContextMenuHeadingTable", "HeadTbl"), // heading paragraph in a table
            Menu("ContextMenuComment",      "Cmt"),     // text inside a comment
            Menu("ContextMenuRevision",     "Rev"),     // a tracked change (what compare produces)
        };

        // Right-clicking ON a hyperlink swaps in the hyperlink-specific menu instead
        // of any of the above, and only the open-link item belongs there: the search
        // and compare items all need selected text.
        private const string HyperlinkMenu = "ContextMenuHyperlink";
        private const string HyperlinkSuffix = "Hl";

        private const string Icon = "getKitveiHakodeshIcon";

        private static string FtsLabel => AddinText.Label("SearchText");
        private static string FtsTip => AddinText.Tip("SearchText");
        private static string CatalogLabel => AddinText.Label("SearchBook");
        private static string CatalogTip => AddinText.Tip("SearchBook");
        private static string CompareLabel => AddinText.Label("CompareText");
        private static string CompareTip => AddinText.Tip("CompareText");
        private static string OpenLinkLabel => AddinText.Label("OpenLink");
        private static string OpenLinkTip => AddinText.Tip("OpenLink");

        /// <summary>
        /// The complete &lt;contextMenus&gt; element, indented to sit directly inside
        /// &lt;customUI&gt;.
        /// </summary>
        public static string Build()
        {
            var sb = new StringBuilder();
            sb.AppendLine("\t<contextMenus>");

            foreach (var menu in Menus)
                AppendFullMenu(sb, menu.Key, menu.Value);

            AppendMenu(sb, HyperlinkMenu, HyperlinkSuffix, openLinkOnly: true);

            sb.Append("\t</contextMenus>");
            return sb.ToString();
        }

        private static void AppendFullMenu(StringBuilder sb, string idMso, string suffix)
        {
            AppendMenu(sb, idMso, suffix, openLinkOnly: false);
        }

        private static void AppendMenu(StringBuilder sb, string idMso, string suffix, bool openLinkOnly)
        {
            sb.AppendLine("\t\t<contextMenu idMso=\"" + idMso + "\">");
            sb.AppendLine("\t\t\t<menuSeparator id=\"KleiKodeshContextSeparator" + suffix + "\"/>");

            if (!openLinkOnly)
            {
                AppendButton(sb, "SearchFtsFromSelection" + suffix,
                             FtsLabel, FtsTip, "contextMenu_Click", "getKitveiHakodeshContextVisible");
                AppendButton(sb, "SearchCatalogFromSelection" + suffix,
                             CatalogLabel, CatalogTip, "contextMenu_Click", "getKitveiHakodeshContextVisible");
                AppendButton(sb, "CompareWithKitveiHakodesh" + suffix,
                             CompareLabel, CompareTip, "compareText_Click", "getKitveiHakodeshContextVisible");
            }

            // getVisible hides this item unless the selection really holds a supported
            // link; Office re-evaluates it on every right-click.
            AppendButton(sb, "OpenLinkInKitveiHakodesh" + suffix,
                         OpenLinkLabel, OpenLinkTip, "openLink_Click", "getContextVisible");

            sb.AppendLine("\t\t</contextMenu>");
        }

        private static void AppendButton(StringBuilder sb, string id, string label, string supertip,
                                         string onAction, string getVisible)
        {
            sb.AppendLine("\t\t\t<button id=\"" + id + "\"");
            sb.AppendLine("\t\t\t\t\tlabel=\"" + Escape(label) + "\"");
            sb.AppendLine("\t\t\t\t\tsupertip=\"" + Escape(supertip) + "\"");
            sb.AppendLine("\t\t\t\t\tgetImage=\"" + Icon + "\"");

            if (getVisible == null)
            {
                sb.AppendLine("\t\t\t\t\tonAction=\"" + onAction + "\"/>");
            }
            else
            {
                sb.AppendLine("\t\t\t\t\tonAction=\"" + onAction + "\"");
                sb.AppendLine("\t\t\t\t\tgetVisible=\"" + getVisible + "\"/>");
            }
        }

        // These labels are authored here rather than supplied by a user, but they do
        // land inside XML attributes, so escape instead of assuming they stay free of
        // ampersands and quotes.
        private static string Escape(string value)
        {
            return value.Replace("&", "&amp;")
                        .Replace("<", "&lt;")
                        .Replace(">", "&gt;")
                        .Replace("\"", "&quot;");
        }

        private static KeyValuePair<string, string> Menu(string idMso, string suffix)
        {
            return new KeyValuePair<string, string>(idMso, suffix);
        }
    }
}
