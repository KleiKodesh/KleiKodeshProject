using DocSeferLib.Helpers;
using Microsoft.Office.Interop.Word;
using System;
using System.Collections.Generic;

namespace DocSeferLib.Paragraphs
{
    public class CenterLastLine : PargaraphsBase
    {
        public void Apply(List<Style> styles, int minLineCount)
        {
            if (!(Vsto.ActiveDocument.Name.EndsWith(".docx", StringComparison.OrdinalIgnoreCase) || string.IsNullOrEmpty( Vsto.ActiveDocument.Path)))
            {
                Alternative(styles, minLineCount);
                return;
            }

            Remove();

            using (new UndoRecordHelper("איפוס טאבים"))
                Vsto.ActiveDocument.DefaultTabStop = 0;

            using (new UndoRecordHelper("מירכוז שורה אחרונה"))
            {
                var paragraphs = ValidParagraphs(Vsto.Selection.Range, styles, minLineCount);
                counter = 0;
                foreach (var paragraph in paragraphs)
                {
                    if (counter++ >= MaxSafeIterations)
                    {
                        counter = 0;
                        System.Windows.Forms.Application.DoEvents();
                    }
                    Range paraRange = paragraph.Range;
                    paraRange.ParagraphFormat.Alignment = WdParagraphAlignment.wdAlignParagraphDistribute;
                    paraRange.End -= 1;
                    paraRange.InsertAfter("\t");
                    paraRange.Characters.Last.Select();
                }
            }
        }

        public void Alternative(List<Style> styles, int minLineCount)
        {
            RemoveAlternative();

            using (new UndoRecordHelper("מירכוז שורה אחרונה"))
            {
                var paragraphs = ValidParagraphs(Vsto.Selection.Range, styles, minLineCount);
                counter = 0;
                foreach (var paragraph in paragraphs)
                {
                    if (counter++ >= MaxSafeIterations)
                    {
                        counter = 0;
                        System.Windows.Forms.Application.DoEvents();
                    }
                    Range paraRange = paragraph.Range;
                    paraRange.ParagraphFormat.Alignment = WdParagraphAlignment.wdAlignParagraphJustify;
                    float centerPos = paraRange.PageSetup.TextColumns.Width / 2;
                    paraRange.ParagraphFormat.TabStops.Add(centerPos, WdTabAlignment.wdAlignTabCenter);
                    paraRange.Characters.Last.Select();
                    Selection selection = Vsto.Selection;
                    selection.HomeKey();
                    selection.TypeText(((char)11) + "\t");
                }
            }
        }

        public void Remove(Range targetRange = null)
        {
            if (!(Vsto.ActiveDocument.Name.EndsWith(".docx", StringComparison.OrdinalIgnoreCase) || string.IsNullOrEmpty(Vsto.ActiveDocument.Path)))
            {
                RemoveAlternative();
                return;
            }

            if (targetRange == null)
                targetRange = Vsto.Selection.Range;

            targetRange.Start = targetRange.Paragraphs.First.Range.Start;
            targetRange.End = targetRange.Paragraphs.Last.Range.End;

            using (new UndoRecordHelper("הסרת מירכוז שורה אחרונה"))
            {
                targetRange.End = targetRange.Paragraphs.Last.Range.End;

                var find = targetRange.Find;
                find.ClearFormatting();
                find.Replacement.ClearFormatting();
                find.ParagraphFormat.Alignment = WdParagraphAlignment.wdAlignParagraphDistribute;
                find.Replacement.ParagraphFormat.Alignment = WdParagraphAlignment.wdAlignParagraphJustify;

                find.Text = "^t^p";
                find.Replacement.Text = "^p";
                find.Forward = true;
                find.Wrap = WdFindWrap.wdFindStop;
                find.Format = true;

                find.Execute(Replace: WdReplace.wdReplaceAll);
            }
        }

        public void RemoveAlternative(Range targetRange = null)
        {
            if (targetRange == null)
                targetRange = Vsto.Selection.Range;

            targetRange.Start = targetRange.Paragraphs.First.Range.Start;
            targetRange.End = targetRange.Paragraphs.Last.Range.End;

            using (new UndoRecordHelper("הסרת מירכוז שורה אחרונה"))
            {
                targetRange.End = targetRange.Paragraphs.Last.Range.End;

                var find2 = targetRange.Find;
                find2.Text = "^l^t";
                find2.Replacement.Text = "";
                find2.Wrap = WdFindWrap.wdFindStop;
                find2.Execute(Replace: WdReplace.wdReplaceAll);
            }
        }
    }
}
