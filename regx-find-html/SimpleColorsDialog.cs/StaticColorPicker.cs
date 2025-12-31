using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace RegexInWord.SimpleColorsDialog
{
    internal class StaticColorPicker
    {
        public static ColorDialog ColorPicker = new ColorDialog();
        public static string HexColor => $"#{ColorPicker.Color.R:X2}{ColorPicker.Color.G:X2}{ColorPicker.Color.B:X2}";
    }
}
