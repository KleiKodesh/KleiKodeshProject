using Microsoft.Office.Interop.Word;

namespace DocSeferLib.Spacing
{
    public static class SpacingHelper
    {
        public static float GetSpaceAfterFromStyle(this Selection selection) =>
            selection.Characters.First.get_Style().ParagraphFormat.SpaceAfter;
        public static float GetSpaceBeforeFromStyle(this Selection selection) =>
            selection.Characters.First.get_Style().ParagraphFormat.SpaceBefore;
        public static float GetLineSpacingFromStyle(this Selection selection) =>
            selection.Characters.First.get_Style().ParagraphFormat.LineSpacing;

        public static float GetSpaceBetweenWords(this Selection selection)
        {
            Range range = selection.Range.Paragraphs[1].Range;
            range.Collapse();
            range.MoveUntil(" ");
            range.MoveEnd();
            return (float)range.Font.Spacing;
        }
    }
}
