using Microsoft.Office.Interop.Word;
using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace KleiKodesh.RegexSearch
{
    public partial class RegexSearch
    {
        Microsoft.Office.Interop.Word.Application App => Globals.ThisAddIn.Application;
        Document Document => App.ActiveDocument;
        Selection Selection => App.Selection;
        int SelectionStart => Selection.Start;

        public void Execute(RegexFind regexFind, RegexFindReplace regexReplace = null, bool replace = false, bool replaceAll = false)
        {
            if (string.IsNullOrEmpty(regexFind.Text))
            {
                System.Windows.Forms.MessageBox.Show("אנא הזן מחרוזת לחיפוש");
                return;
            }

            ComputeEffectivePattern(regexFind);

            if (replace && regexReplace != null)
                Replace(regexFind, regexReplace);

            Results = GetSearchResults(regexFind);
            if (Results == null || Results.Length == 0)
                return;

            if (replaceAll && regexReplace != null)
            {
                ReplaceAll(regexFind, regexReplace);
                return;
            }

            // Select appropriate result based on mode
            if (regexFind.Mode == RegexSearchMode.Back)
                SelectPreviousResult();
            else
                SelectNextResult();
        }

        void ComputeEffectivePattern(RegexFind find)
        {
            if (!find.UseWildcards)
                Regex.Escape(find.Text);

            if (find.Slop > 0)
            {
                string[] terms = find.Text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (terms.Length > 1)
                {
                    string joiner = $@"\b\W+(?:[\w""]+\W+){{0,{find.Slop}}}";
                    find.Text = string.Join(joiner, terms);
                }
            }
        }

        public RegexFindBase GetSelectionFormatting()
        {
            try
            {
                var selection = Selection;
                if (selection == null || selection.Range == null)
                    return new RegexFindBase();

                dynamic rng = selection.Range;
                
                // Check both regular and Bi properties - treat as one (for Hebrew/Arabic support)
                bool? bold = (rng.Font.Bold == -1 || rng.Font.BoldBi == -1) ? true 
                           : (rng.Font.Bold == 0 && rng.Font.BoldBi == 0) ? false 
                           : (bool?)null;
                
                bool? italic = (rng.Font.Italic == -1 || rng.Font.ItalicBi == -1) ? true 
                             : (rng.Font.Italic == 0 && rng.Font.ItalicBi == 0) ? false 
                             : (bool?)null;
                
                // For font size, prefer SizeBi if it's valid, otherwise use Size
                float? fontSize = rng.Font.SizeBi > 0 ? (float)rng.Font.SizeBi 
                                : rng.Font.Size > 0 ? (float)rng.Font.Size 
                                : (float?)null;
                
                // For font name, prefer NameBi if it's set, otherwise use Name
                string fontName = !string.IsNullOrEmpty(rng.Font.NameBi) ? rng.Font.NameBi 
                                : rng.Font.Name ?? "";

                // Get color value with detailed debugging
                int colorValue = (int)rng.Font.Color;
                System.Diagnostics.Debug.WriteLine($"COLOR DEBUG - GetSelectionFormatting:");
                System.Diagnostics.Debug.WriteLine($"  Raw Font.Color: {rng.Font.Color}");
                System.Diagnostics.Debug.WriteLine($"  Cast to int: {colorValue}");
                System.Diagnostics.Debug.WriteLine($"  Hex representation: 0x{((uint)colorValue):X8}");

                return new RegexFindBase
                {
                    Bold = bold,
                    Italic = italic,
                    Underline = rng.Font.Underline != (int)WdUnderline.wdUnderlineNone,
                    Superscript = rng.Font.Superscript == -1 ? true : (rng.Font.Superscript == 0 ? false : (bool?)null),
                    Subscript = rng.Font.Subscript == -1 ? true : (rng.Font.Subscript == 0 ? false : (bool?)null),
                    Style = (rng.Style as Style)?.NameLocal ?? "",
                    Font = fontName,
                    FontSize = fontSize,
                    TextColor = (int)rng.Font.Color
                };
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error getting selection formatting: {ex.Message}");
                return new RegexFindBase();
            }
        }
    }

}