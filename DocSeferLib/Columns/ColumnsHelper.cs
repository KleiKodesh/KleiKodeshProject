using DocSeferLib.Helpers;
using Microsoft.Office.Interop.Word;
using System;
using System.Collections.Generic;

namespace DocSeferLib.Columns
{
    public static class ColumnsHelper
    {
        public static RangePageData RangePageCount(this Range range)
        {
            var actionRange = range.Duplicate;
            int lastPage = (int)actionRange.Information[WdInformation.wdActiveEndPageNumber];
            actionRange.Collapse(WdCollapseDirection.wdCollapseStart);
            int firstPage = (int)actionRange.Information[WdInformation.wdActiveEndPageNumber];
            return new RangePageData { LastPage = lastPage, FirstPage = firstPage };
        }

        public static List<Range> RangeSections(this Range range)
        {
            if (range.Sections.Count < 2)
                return new List<Range> { range.Duplicate };

            List<Range> result = new List<Range>();
            foreach (Section section in range.Sections)
            {
                Range sectionRange = section.Range;
                sectionRange.Start = Math.Max(sectionRange.Start, range.Start);
                sectionRange.End = Math.Min(sectionRange.End, range.End);
                result.Add(sectionRange);
            }

            return result;
        }

        public static int ColumnBreakPoint(this Range range)
        {
            using (new ScreenFreeze())
            {
                range.Select();
                Selection selection = Vsto.Selection;
                selection.Collapse(WdCollapseDirection.wdCollapseStart);

                int originalPos = (int)selection.Information[WdInformation.wdVerticalPositionRelativeToPage];
                int lastGoodPos = originalPos;

                while (true)
                {
                    selection.Move(WdUnits.wdLine, 1);
                    int currentPos = (int)selection.Information[WdInformation.wdVerticalPositionRelativeToPage];

                    if (currentPos <= lastGoodPos) // Page/column break likely occurred
                        break;

                    lastGoodPos = currentPos;
                }
                
               return selection.Start;
            }
        }

    }
}
