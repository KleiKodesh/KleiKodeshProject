using System.Windows.Media;
using WpfLib;

namespace RegexFindLib.UI
{
    /// <summary>
    /// Holds the formatting filter/apply options for one side (find or replace).
    /// Bound directly to the formatting panel in the XAML view.
    /// </summary>
    public class FormattingOptions : ViewModelBase
    {
        bool? _bold;
        /// <summary>Gets or sets whether bold formatting is applied. Null means "don't filter/apply".</summary>
        public bool? Bold { get => _bold; set => SetProperty(ref _bold, value); }

        bool? _italic;
        /// <summary>Gets or sets whether italic formatting is applied. Null means "don't filter/apply".</summary>
        public bool? Italic { get => _italic; set => SetProperty(ref _italic, value); }

        bool? _underline;
        /// <summary>Gets or sets whether underline formatting is applied. Null means "don't filter/apply".</summary>
        public bool? Underline { get => _underline; set => SetProperty(ref _underline, value); }

        bool? _superscript;
        /// <summary>
        /// Gets or sets whether superscript formatting is applied.
        /// Null means "don't filter/apply". Setting to true clears Subscript.
        /// </summary>
        public bool? Superscript
        {
            get => _superscript;
            set
            {
                if (SetProperty(ref _superscript, value) && value == true)
                    Subscript = null;  // Mutual exclusion: can't be both super and subscript
            }
        }

        bool? _subscript;
        /// <summary>
        /// Gets or sets whether subscript formatting is applied.
        /// Null means "don't filter/apply". Setting to true clears Superscript.
        /// </summary>
        public bool? Subscript
        {
            get => _subscript;
            set
            {
                if (SetProperty(ref _subscript, value) && value == true)
                    Superscript = null;  // Mutual exclusion: can't be both super and subscript
            }
        }

        string _fontName = "";
        /// <summary>Gets or sets the font name filter/apply value. Empty string means "don't filter/apply".</summary>
        public string FontName { get => _fontName; set => SetProperty(ref _fontName, value); }

        float _fontSize = 0f;
        /// <summary>Gets or sets the font size filter/apply value in points. Zero means "don't filter/apply".</summary>
        public float FontSize { get => _fontSize; set => SetProperty(ref _fontSize, value); }

        string _styleName = "";
        /// <summary>Gets or sets the paragraph style name filter/apply value. Empty string means "don't filter/apply".</summary>
        public string StyleName { get => _styleName; set => SetProperty(ref _styleName, value); }

        Color? _textColor;
        /// <summary>WPF display color. Converted to/from Word decimal at the Word boundary.</summary>
        public Color? TextColor { get => _textColor; set => SetProperty(ref _textColor, value); }

        /// <summary>Returns true if any formatting option has a non-default value.</summary>
        public bool HasAny =>
            Bold.HasValue || Italic.HasValue || Underline.HasValue ||
            Superscript.HasValue || Subscript.HasValue ||
            !string.IsNullOrEmpty(FontName) || FontSize > 0 ||
            !string.IsNullOrEmpty(StyleName) || TextColor.HasValue;

        /// <summary>Resets all formatting options to their default (unset) values.</summary>
        public void Clear()
        {
            Bold = null;
            Italic = null;
            Underline = null;
            Superscript = null;
            Subscript = null;
            FontName = "";
            FontSize = 0f;
            StyleName = "";
            TextColor = null;
        }
    }
}
