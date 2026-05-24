using Microsoft.Office.Interop.Word;
using Nakdan.WdStyles;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Nakdan.Helpers
{
    public static class VstoHelper
    {
        public static Microsoft.Office.Interop.Word.Application Application { get; set; }
        public static Microsoft.Office.Tools.Word.ApplicationFactory ApplicationFactory { get; set; }

        public static Document ActiveDocument => Application?.ActiveDocument;

        public static IEnumerable<Style> ActiveStyles =>
            ActiveDocument?.Styles.Cast<Style>().Where(s => s.InUse) ?? Enumerable.Empty<Style>();

        public static DocumentStyleProvider StyleProvider =>
            new DocumentStyleProvider(Application);
    }
}
