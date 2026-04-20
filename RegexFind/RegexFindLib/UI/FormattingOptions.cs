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
        public bool? Bold { get => _bold; set => SetProperty(ref _bold, value); }

        bool? _italic;
        public bool? Italic { get => _italic; set => SetProperty(ref _italic, value); }

        bool? _underline;
        public bool? Underline { get => _underline; set => SetProperty(ref _underline, value); }

        bool? _superscript;
        public bool? Superscript { get => _superscript; set => SetProperty(ref _superscript, value); }

        bool? _subscript;
        public bool? Subscript { get => _subscript; set => SetProperty(ref _subscript, value); }

        string _fontName = "";
        public string FontName { get => _fontName; set => SetProperty(ref _fontName, value); }

        float _fontSize = 0f;
        public float FontSize { get => _fontSize; set => SetProperty(ref _fontSize, value); }

        string _styleName = "";
        public string StyleName { get => _styleName; set => SetProperty(ref _styleName, value); }

        Color? _textColor;
        public Color? TextColor { get => _textColor; set => SetProperty(ref _textColor, value); }

        public bool HasAny =>
            Bold.HasValue || Italic.HasValue || Underline.HasValue ||
            Superscript.HasValue || Subscript.HasValue ||
            !string.IsNullOrEmpty(FontName) || FontSize > 0 ||
            !string.IsNullOrEmpty(StyleName) || TextColor.HasValue;

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
