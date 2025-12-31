using RegexInWord.Helpers;
using System.Collections.Generic;

namespace RegexInWord.SimpleColorsDialog
{
    public class ColorsHelper : VstoBase
    {
        public class ColorModel
        {
            public string HexValue { get; set; }
            public int DecimalValue { get; set; }
        }

        public List<ColorModel> StandardOfficeColors { get; private set; }
        public List<ColorModel> ThemeColors { get; private set; }

        public ColorsHelper()
        {
            PopulateColors();
        }

        public void PopulateColors()
        {
            StandardOfficeColors = new List<ColorModel>
            {
                new ColorModel { HexValue = "#C00000", DecimalValue = Hex.HexToInt("#C00000") ?? 0},
                new ColorModel { HexValue = "#FF0000", DecimalValue = Hex.HexToInt("#FF0000") ?? 0},
                new ColorModel { HexValue = "#FFC000", DecimalValue = Hex.HexToInt("#FFC000") ?? 0},
                new ColorModel { HexValue = "#FFFF00", DecimalValue = Hex.HexToInt("#FFFF00") ?? 0},
                new ColorModel { HexValue = "#92D050", DecimalValue = Hex.HexToInt("#92D050") ?? 0},
                new ColorModel { HexValue = "#00B050", DecimalValue = Hex.HexToInt("#00B050") ?? 0},
                new ColorModel { HexValue = "#00B0F0", DecimalValue = Hex.HexToInt("#00B0F0") ?? 0},
                new ColorModel { HexValue = "#0070C0", DecimalValue = Hex.HexToInt("#0070C0") ?? 0},
                new ColorModel { HexValue = "#002060", DecimalValue = Hex.HexToInt("#002060") ?? 0},
                new ColorModel { HexValue = "#7030A0", DecimalValue = Hex.HexToInt("#7030A0") ?? 0},
            };

            ThemeColors = new List<ColorModel>
            {
                new ColorModel { HexValue = ThemeColorsHelper.WdToWpfHex("DC00FFFF"), DecimalValue = -603914241},
                new ColorModel { HexValue = ThemeColorsHelper.WdToWpfHex("DD00FFFF"), DecimalValue = -587137025},
                new ColorModel { HexValue = ThemeColorsHelper.WdToWpfHex("DE00FFFF"), DecimalValue = -570359809},
                new ColorModel { HexValue = ThemeColorsHelper.WdToWpfHex("DF00FFFF"), DecimalValue = -553582593},
                new ColorModel { HexValue = ThemeColorsHelper.WdToWpfHex("D400FFFF"), DecimalValue = -738131969},
                new ColorModel { HexValue = ThemeColorsHelper.WdToWpfHex("D500FFFF"), DecimalValue = -721354753},
                new ColorModel { HexValue = ThemeColorsHelper.WdToWpfHex("D600FFFF"), DecimalValue = -704577537},
                new ColorModel { HexValue = ThemeColorsHelper.WdToWpfHex("D700FFFF"), DecimalValue = -687800321},
                new ColorModel { HexValue = ThemeColorsHelper.WdToWpfHex("D800FFFF"), DecimalValue = -671023105},
                new ColorModel { HexValue = ThemeColorsHelper.WdToWpfHex("D900FFFF"), DecimalValue = -654245889},
            };
        }
    }
}
