using Microsoft.Office.Interop.Word;
using System;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace RegexFindLib.Search
{
    public partial class RegexSearch
    {
        public void ReplaceAll(RegexFind find, RegexFindReplace replace)
        {
            using (_ = new Helpers.WdActionManager(_word, "החלפה מרובה", saveRange: true))
                for (int i = Results.Length - 1; i >= 0; i--)
                    ApplyReplace(Results[i].Range, find, replace);
        }

        void Replace(RegexFind find, RegexFindReplace replace)
        {
            try
            {
                using (_ = new Helpers.WdActionManager(_word, "החלפה", doEvents: false))
                    ApplyReplace(Selection.Range, find, replace);
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.Message);
            }
        }

        void ApplyReplace(dynamic rng, RegexFind find, RegexFindReplace replace)
        {
            var regex = new Regex(find.Text);
            var match = regex.Match(rng.Text);
            if (!match.Success) return;

            rng.Start += match.Index;
            rng.End = rng.Start + match.Length;

            int startPos = rng.Start;
            string newText = regex.Replace(rng.Text ?? "", replace.Text ?? "");
            rng.Text = newText;

            if (newText.Length == 0) return;

            rng.SetRange(startPos, startPos + newText.Length);

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
                rng.Font.Underline = replace.Underline.Value
                    ? WdUnderline.wdUnderlineSingle : WdUnderline.wdUnderlineNone;
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
                rng.Font.Color = replace.TextColor.Value;
        }
    }
}
