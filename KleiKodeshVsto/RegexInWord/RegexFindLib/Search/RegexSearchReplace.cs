using Microsoft.Office.Interop.Word;
using System;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace RegexFindLib.Search
{
    public partial class RegexSearch
    {
        void ReplaceAll(FindRequest find)
        {
            using (_ = new Helpers.WdActionManager(_word, "החלפה מרובה", saveRange: true))
                for (int i = Results.Length - 1; i >= 0; i--)
                    ApplyReplace(Results[i].Range, find);
        }

        void Replace(FindRequest find)
        {
            try
            {
                using (_ = new Helpers.WdActionManager(_word, "החלפה", doEvents: false))
                    ApplyReplace(Selection.Range, find);
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.Message);
            }
        }

        void ApplyReplace(dynamic rng, FindRequest find)
        {
            var replacement = find.Replacement;
            var regex = new Regex(find.Text);
            var match = regex.Match(rng.Text);
            if (!match.Success) return;

            rng.Start += match.Index;
            rng.End    = rng.Start + match.Length;

            int    startPos = rng.Start;
            string newText  = regex.Replace(rng.Text ?? "", replacement.Text ?? "");
            rng.Text = newText;

            if (newText.Length == 0) return;

            rng.SetRange(startPos, startPos + newText.Length);

            var fmt = replacement.Formatting;
            if (fmt == null) return;

            if (fmt.Bold.HasValue)
            {
                rng.Font.Bold   = fmt.Bold.Value ? -1 : 0;
                rng.Font.BoldBi = fmt.Bold.Value ? -1 : 0;
            }
            if (fmt.Italic.HasValue)
            {
                rng.Font.Italic   = fmt.Italic.Value ? -1 : 0;
                rng.Font.ItalicBi = fmt.Italic.Value ? -1 : 0;
            }
            if (fmt.Underline.HasValue)
                rng.Font.Underline = fmt.Underline.Value
                    ? WdUnderline.wdUnderlineSingle : WdUnderline.wdUnderlineNone;
            if (fmt.Superscript.HasValue)
                rng.Font.Superscript = fmt.Superscript.Value ? -1 : 0;
            if (fmt.Subscript.HasValue)
                rng.Font.Subscript = fmt.Subscript.Value ? -1 : 0;
            if (!string.IsNullOrEmpty(fmt.Style))
                rng.Style = fmt.Style;
            if (!string.IsNullOrEmpty(fmt.FontName))
            {
                rng.Font.Name   = fmt.FontName;
                rng.Font.NameBi = fmt.FontName;
            }
            if (fmt.FontSize.HasValue)
            {
                rng.Font.Size   = fmt.FontSize.Value;
                rng.Font.SizeBi = fmt.FontSize.Value;
            }
            if (fmt.TextColor.HasValue)
                rng.Font.Color = fmt.TextColor.Value;
        }
    }
}
