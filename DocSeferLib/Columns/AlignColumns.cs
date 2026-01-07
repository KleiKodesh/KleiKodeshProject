using DocSeferLib.Helpers;
using Microsoft.Office.Interop.Word;
using System;
using System.Collections.Generic;
using System.Linq;
using WpfLib;
using WpfLib.Helpers;

//maybe first apply space after auto before detecting longer column
namespace DocSeferLib.Columns
{
    public class AlignColumns : ViewModelBase
    {
        class ColumnObject
        {
            public Range Range { get; set; }
            public Range Bottom { get; set; }
            public float yPos { get; set; }
        }

        int _maxSpacingChange = 40;
        public int MaxSpaceAfter { get => _maxSpacingChange; set => SetProperty(ref _maxSpacingChange, value); }
        //public float MinSpacingChange { get; set; } = 0.1f;

        public void FindNext(bool repeat = true)
        {
            Document document = Vsto.ActiveDocument;
            Range selectionRange = Vsto.Selection.Range;
            Range actionRange = document.Range(selectionRange.Start, document.Content.End);

            var pageData = actionRange.RangePageCount();
            int totalPages = document.ComputeStatistics(WdStatistic.wdStatisticPages);
            if (pageData.FirstPage < 2) repeat = false;

            for (int i = pageData.FirstPage; i <= pageData.LastPage; i++)
            {
                if (i % 25 == 0)
                    System.Windows.Forms.Application.DoEvents();

                try
                {
                    using (new ScreenFreeze())
                    {
                        Range pageRange = document.GoTo(WdGoToItem.wdGoToPage, WdGoToDirection.wdGoToAbsolute, i);
                        pageRange.End = (i < totalPages)
                            ? document.GoTo(WdGoToItem.wdGoToPage, WdGoToDirection.wdGoToAbsolute, i + 1).Start - 1
                            : document.Content.End;

                        var sectionRanges = pageRange.RangeSections().Where(r => r.PageSetup.TextColumns.Count == 2);
                        foreach (Range sectionRange in sectionRanges)
                        {
                            var columns = GetColumns(document, sectionRange);

                            if (columns[0].yPos != columns[1].yPos)
                            {
                                columns[0].Bottom.Select();
                                Vsto.Selection.HomeKey(WdUnits.wdLine, WdMovementType.wdExtend);
                                if (!(selectionRange.Start >= Vsto.Selection.Range.Start && selectionRange.End <= Vsto.Selection.Range.End))
                                    return;
                            }
                                
                        }
                    }
                }
                catch { }
            }

            if (!repeat)
            {
                MsgBox.Information("לא נמצאו תוצאות");
                return;
            }

            var result = MsgBox.Question("לא נמצאו תוצאות. האם ברצונך לחפש מתחילת המסמך");
            if (result == System.Windows.MessageBoxResult.Yes)
            {
                document.Content.Characters.First.Select();
                FindNext(false);
            }
        }

        public void Apply()
        {
            using (new UndoRecordHelper("יישור טורים"))
            {
                Document document = Vsto.ActiveDocument;
                var pageData = Vsto.Selection.Range.RangePageCount();
                int totalPages = document.ComputeStatistics(WdStatistic.wdStatisticPages);

                for (int i = pageData.FirstPage; i <= pageData.LastPage; i++)
                {
                    if (i % 25 == 0) 
                        System.Windows.Forms.Application.DoEvents();

                    try
                    {
                        using (new ScreenFreeze())
                        {
                            Range pageRange = document.GoTo(WdGoToItem.wdGoToPage, WdGoToDirection.wdGoToAbsolute, i);
                            pageRange.End = (i < totalPages)
                                ? document.GoTo(WdGoToItem.wdGoToPage, WdGoToDirection.wdGoToAbsolute, i + 1).Start - 1
                                : document.Content.End;

                            var sectionRanges = pageRange.RangeSections().Where(r => r.PageSetup.TextColumns.Count == 2);
                            foreach (Range sectionRange in sectionRanges)
                            {
                                var columns = GetColumns(document, sectionRange);

                                if (columns[0].yPos == columns[1].yPos)
                                    continue;

                                Align(columns.OrderBy(c => c.yPos).ToList());
                            }
                        }
                    }
                    catch { }
                }
            }
        }

        List<ColumnObject> GetColumns(Document document, Range range)
        {
            int breakPoint = range.ColumnBreakPoint();

            Range column1 = document.Range(range.Start, breakPoint - 1);
            Range column1Bottom = document.Range(breakPoint - 1, breakPoint - 1);
            float column1_y = (float)column1Bottom.Information[WdInformation.wdVerticalPositionRelativeToPage];

            Range column2 = document.Range(breakPoint, range.End);
            Range column2Bottom = document.Range(range.End, range.End);
            float column2_y = (float)column2Bottom.Information[WdInformation.wdVerticalPositionRelativeToPage];

            return new List<ColumnObject>()
            {
                new ColumnObject { Range = column1, Bottom = column1Bottom, yPos = column1_y },
                new ColumnObject { Range = column2, Bottom = column2Bottom, yPos = column2_y }
            };
        }

        void Align(List<ColumnObject> columns)
        {
            int counter = 0;
            while (columns[0].yPos < columns[1].yPos && counter++ < 5)
            {
                float diff = columns[1].yPos - columns[0].yPos;
                var paragraphs = columns[0].Range.Paragraphs.Cast<Paragraph>().ToList();

                int usableCount = paragraphs.Count - 1;
                if (usableCount == 0)
                    break;

                float increment = Math.Min(diff / usableCount, MaxSpaceAfter);
                bool changed = false;

                if (increment < 0)
                    changed |= IncreaseSpaceAfter(paragraphs[0], Math.Min(diff, MaxSpaceAfter));
                else
                    for (int i = 0; i < usableCount; i++)
                        changed |= IncreaseSpaceAfter(paragraphs[i], increment);

                if (!changed)
                    break;

                Vsto.Application.ScreenRefresh();
                columns[0].yPos = (float)columns[0].Bottom.Information[WdInformation.wdVerticalPositionRelativeToPage];
                columns[1].yPos = (float)columns[1].Bottom.Information[WdInformation.wdVerticalPositionRelativeToPage];
            }
        }


        bool IncreaseSpaceAfter(Paragraph paragraph, float increment)
        {
            float current = paragraph.SpaceAfter;
            if (current >= MaxSpaceAfter) return false;
            float newSpace = Math.Min(current + increment, MaxSpaceAfter);
            paragraph.SpaceAfter = newSpace;
            return true;
        }


        //void Align(List<ColumnObject> columns)
        //{
        //    const float minSpacingChange = 0.1f;
        //    int loopCount = 0;


        //    while (Math.Abs(columns[0].yPos - columns[1].yPos) > 0.5 && loopCount++ < 5)
        //    {
        //        float diff = columns[0].yPos - columns[1].yPos;
        //        var paragraphs = columns[0].Range.Paragraphs.Cast<Paragraph>().ToList();
        //        if (paragraphs.Count < 2)
        //            break;

        //        // Try to find the minimum count of paragraphs to apply spacing
        //        int usableCount = paragraphs.Count - 1; // Don't use the last paragraph
        //        while (usableCount > 0 && Math.Abs(diff / usableCount) < minSpacingChange)
        //        {
        //            usableCount--;
        //        }

        //        if (usableCount == 0)
        //            break;

        //        float increment = diff / usableCount;

        //        for (int i = 0; i < usableCount; i++)
        //        {
        //            paragraphs[i].SpaceAfter += Math.Abs(increment);
        //        }

        //        Vsto.Application.ScreenRefresh();
        //        columns[0].yPos = (float)columns[0].Bottom.Information[WdInformation.wdVerticalPositionRelativeToPage];
        //        columns[1].yPos = (float)columns[1].Bottom.Information[WdInformation.wdVerticalPositionRelativeToPage];
        //    }
        //}
    }
}
