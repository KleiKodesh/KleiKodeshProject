using KleiKodesh.Helpers;
using Microsoft.Office.Interop.Word;
using System;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace KleiKodesh.RegexSearch
{
    public partial class RegexSearch
    {
        public void ReplaceAll(RegexFind find, RegexFindReplace replace)
        {
            using (_ = new WdActionManager("החלפה מרובה", saveRange: true))
                for (int i = Results.Length - 1; i >= 0; i--)
                    ApplyReplace(Results[i].Range, find, replace);
        }

        void Replace(RegexFind find, RegexFindReplace replace)
        {
            try
            {
                using (_ = new WdActionManager("החלפה", doEvents: false))
                    ApplyReplace(Selection.Range, find, replace);
            }
            catch (Exception ex) 
            {
                Debug.Write(ex.Message);
            }
        }

        //need to use dynamic to expose decimal text color prop
        void ApplyReplace(dynamic rng, RegexFind find, RegexFindReplace replace)
        {
            var regex = new Regex(find.Text);
            var match = regex.Match(rng.Text);
            if (!match.Success)
                return;

            rng.Start += match.Index;
            rng.End = rng.Start + match.Length;
            
            // Store the start position before replacing text
            int startPos = rng.Start;
            string newText = regex.Replace(rng.Text ?? "", replace.Text ?? "");
            rng.Text = newText;
            
#if DEBUG
            System.Diagnostics.Debug.WriteLine($"=== APPLY REPLACE DEBUG ===");
            System.Diagnostics.Debug.WriteLine($"New text: '{newText}', Length: {newText.Length}");
            System.Diagnostics.Debug.WriteLine($"StartPos: {startPos}, EndPos: {startPos + newText.Length}");
            System.Diagnostics.Debug.WriteLine($"Replace Bold: {replace.Bold}, Italic: {replace.Italic}");
            System.Diagnostics.Debug.WriteLine($"Replace FontSize: {replace.FontSize}, Font: '{replace.Font}'");
            System.Diagnostics.Debug.WriteLine($"Replace Style: '{replace.Style}', TextColor: {replace.TextColor}");
#endif

            // Only apply formatting if there's text to format
            if (newText.Length == 0)
                return;
                
            // After setting Text, the range collapses - re-select the replaced text
            rng.SetRange(startPos, startPos + newText.Length);

#if DEBUG
            System.Diagnostics.Debug.WriteLine($"Range after SetRange - Start: {rng.Start}, End: {rng.End}, Text: '{rng.Text}'");
#endif

            // Apply formatting - set both regular and Bi properties for Hebrew/Arabic support
            if (replace.Bold.HasValue) 
            { 
                rng.Font.Bold = replace.Bold.Value ? -1 : 0; 
                rng.Font.BoldBi = replace.Bold.Value ? -1 : 0; 
            }
            if (replace.Italic.HasValue) 
            { 
                rng.Font.Italic = replace.Italic.Value ? -1 : 0; 
                rng.Font.ItalicBi = replace.Italic.Value ? -1 : 0; 
            }
            if (replace.Underline.HasValue) 
                rng.Font.Underline = replace.Underline.Value ? WdUnderline.wdUnderlineSingle : WdUnderline.wdUnderlineNone;
            if (replace.Superscript.HasValue) 
                rng.Font.Superscript = replace.Superscript.Value ? -1 : 0;
            if (replace.Subscript.HasValue) 
                rng.Font.Subscript = replace.Subscript.Value ? -1 : 0;
            if (!string.IsNullOrEmpty(replace.Style)) 
                rng.Style = replace.Style;
            if (!string.IsNullOrEmpty(replace.Font)) 
            { 
                rng.Font.Name = replace.Font; 
                rng.Font.NameBi = replace.Font; 
            }
            if (replace.FontSize.HasValue) 
            { 
                rng.Font.Size = replace.FontSize.Value; 
                rng.Font.SizeBi = replace.FontSize.Value; 
            }
            if (replace.TextColor.HasValue)
            {
                rng.Font.Color = (int)replace.TextColor;
            }
            
#if DEBUG
            System.Diagnostics.Debug.WriteLine($"Formatting applied successfully");
            System.Diagnostics.Debug.WriteLine($"=== APPLY REPLACE DEBUG END ===");
#endif
        }
    }
}