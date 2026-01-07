using Microsoft.Office.Interop.Word;
using System.Collections.Generic;
using System.Linq;

public static class Vsto
{
    public static Microsoft.Office.Tools.Word.ApplicationFactory ApplicationFactory { get; set; }
    public static Application Application { get; set; }
    public static UndoRecord UndoRecord => Application?.UndoRecord;
    public static Selection Selection => Application.Selection;
    public static Document ActiveDocument => Application.ActiveDocument;
    public static IEnumerable<Style> ActiveStyles => ActiveDocument?.Styles.Cast<Style>().Where(s => s.InUse);
}

