using DocSeferLib.Helpers;
using Microsoft.Office.Interop.Word;
using System.Collections.Generic;

namespace DocSeferLib.Paragraphs
{
    public class FirstWordHanging : PargaraphsBase
    {
        static readonly string HardBreak = "\v" + (char)160;

        public void Apply(List<Style> styles, int minLineCount)
        {
            Remove();
            var selectionRange = Vsto.Application.Selection.Range;

            using (new UndoRecordHelper("עיצוב חלון"))
            {
                PrepareFootnotes(selectionRange);
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
                    paraRange.Collapse(WdCollapseDirection.wdCollapseStart);
                    paraRange.MoveUntil(" ");
                    paraRange.Move();
                    float firstWordX = (float)paraRange.Information[WdInformation.wdHorizontalPositionRelativeToTextBoundary];

                    paraRange.Select();

                    Vsto.Selection.EndKey();
                    Vsto.Selection.Text = HardBreak;
                    Vsto.Selection.Collapse(WdCollapseDirection.wdCollapseEnd);
                    float insertX = (float)Vsto.Selection.Information[WdInformation.wdHorizontalPositionRelativeToTextBoundary];
                    Vsto.Selection.Previous().Font.Spacing = insertX - firstWordX;
                }
            }
        }

        public void DoubleWindow(List<Style> styles, int minLineCount)
        {
            Remove();

            var selectionRange = Vsto.Application.Selection.Range;

            using (new UndoRecordHelper("עיצוב חלון"))
            {
                PrepareFootnotes(selectionRange);
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
                    paraRange.Collapse();
                    paraRange.MoveUntil(" ");
                    paraRange.Move();
                    float firstWordX = (float)paraRange.Information[WdInformation.wdHorizontalPositionRelativeToTextBoundary];

                    paraRange.Select();

                    Vsto.Selection.EndKey();
                    Vsto.Selection.Text = HardBreak;
                    Vsto.Selection.Collapse(WdCollapseDirection.wdCollapseEnd);
                    float insertX = (float)Vsto.Selection.Information[WdInformation.wdHorizontalPositionRelativeToTextBoundary];
                    Vsto.Selection.Previous().Font.Spacing = insertX - firstWordX;

                    Vsto.Selection.EndKey();
                    Vsto.Selection.Text = HardBreak;
                    Vsto.Selection.Collapse(WdCollapseDirection.wdCollapseEnd);
                    Vsto.Selection.Previous().Font.Spacing = insertX - firstWordX;
                }
            }
        }

        public void Remove(Range targetRange = null)
        {
            if (targetRange == null)
                targetRange = Vsto.Selection.Range;

            targetRange.Start = targetRange.Paragraphs.First.Range.Start;
            targetRange.End = targetRange.Paragraphs.Last.Range.End;

            using (new UndoRecordHelper("הסרת עיצוב חלון"))
            {
                var find = targetRange.Find;
                find.Text = HardBreak;
                find.Replacement.Text = "";
                find.Wrap = WdFindWrap.wdFindStop;
                find.Execute(Replace: WdReplace.wdReplaceAll);
            }
        }
    }
}
