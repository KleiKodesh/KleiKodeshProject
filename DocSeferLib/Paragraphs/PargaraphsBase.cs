using Microsoft.Office.Interop.Word;
using System.Collections.Generic;
using System.Linq;
using WpfLib;

namespace DocSeferLib.Paragraphs
{
    public class PargaraphsBase : ViewModelBase
    {
        public List<Paragraph> ValidParagraphs(Range targetRange, List<Style> styles, int minLineCount) =>
           targetRange.Paragraphs
              .Cast<Paragraph>()
              .Where(p =>
              {
                  var style = p.get_Style() as Style;
                  int lines = p.Range.ComputeStatistics(WdStatistic.wdStatisticLines);
                  return style != null && styles.Any(s => s.NameLocal == style.NameLocal) && lines >= minLineCount;
              }).ToList();

        protected int counter = 0;
        protected const int MaxSafeIterations = 50;
        public void PrepareFootnotes(Range range)
        {
            if (!(bool)range.Information[WdInformation.wdInFootnote] && !(bool)range.Information[WdInformation.wdInEndnote])
                return;

            range.Start = range.Paragraphs.First.Range.Start;

            var find = range.Find;
            find.Wrap = WdFindWrap.wdFindStop;

            find.Text = "^f";
            find.Replacement.Text = "^&%%" + (char)160;
            find.Execute(Replace: WdReplace.wdReplaceAll);

            find.Text = "%%" + (char)160;
            find.Replacement.Text = ((char)160).ToString();
            find.Execute(Replace: WdReplace.wdReplaceAll);
        }
    }
}
