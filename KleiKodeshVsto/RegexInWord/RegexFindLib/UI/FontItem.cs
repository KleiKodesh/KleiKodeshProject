namespace RegexFindLib.UI
{
    /// <summary>
    /// Represents a single font in the font picker. IsHebrew comes from
    /// WpfLib.Helpers.FontsProvider, the solution's shared DirectWrite font source — the same
    /// enumeration and the same א glyph test the Kitvei Hakodesh font picker uses.
    /// </summary>
    public class FontItem
    {
        public string Name     { get; }
        public bool   IsHebrew { get; }

        /// <summary>Preview text shown in the dropdown — Hebrew sample for Hebrew fonts,
        /// Latin sample for all others.</summary>
        public string Preview  => IsHebrew ? "אבגד הוז" : "ABC abc";

        public FontItem(string name, bool isHebrew)
        {
            Name     = name;
            IsHebrew = isHebrew;
        }

        // So the editable ComboBox can display/match by name
        public override string ToString() => Name;
    }
}
