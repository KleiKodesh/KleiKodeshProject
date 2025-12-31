using System;
using System.Globalization;
using System.Windows.Media;

namespace RegexInWord.SimpleColorsDialog
{
    public static class Hsl
    {
        public static (double Hue, double Saturation, double Lightness) HexToHsl(string hex)
        {
            if (!int.TryParse(hex.Replace("#", ""), NumberStyles.HexNumber, null, out int intColor))
                return (0, 0, 0);

            byte r = (byte)((intColor >> 16) & 0xFF);
            byte g = (byte)((intColor >> 8) & 0xFF);
            byte b = (byte)(intColor & 0xFF);

            Color color = Color.FromRgb(r, g, b);
            return RgbToHsl(color);
        }

        static (double Hue, double Saturation, double Lightness) RgbToHsl(Color color)
        {
            RgbToHsl(color.R, color.G, color.B, out double h, out double s, out double l);
            return (h, s, l);
        }

        public static void RgbToHsl(byte r, byte g, byte b, out double h, out double s, out double l)
        {
            double rD = r / 255.0;
            double gD = g / 255.0;
            double bD = b / 255.0;

            double max = Math.Max(rD, Math.Max(gD, bD));
            double min = Math.Min(rD, Math.Min(gD, bD));
            h = s = 0;
            l = (max + min) / 2.0;

            if (max != min)
            {
                double d = max - min;
                s = l > 0.5 ? d / (2.0 - max - min) : d / (max + min);

                if (max == rD)
                    h = (gD - bD) / d + (gD < bD ? 6 : 0);
                else if (max == gD)
                    h = (bD - rD) / d + 2;
                else
                    h = (rD - gD) / d + 4;

                h /= 6.0;
            }
        }

        public static void HslToRgb(double h, double s, double l, out byte r, out byte g, out byte b)
        {
            double rD, gD, bD;

            if (s == 0)
            {
                rD = gD = bD = l; // achromatic
            }
            else
            {
                double q = l < 0.5 ? l * (1 + s) : l + s - l * s;
                double p = 2 * l - q;
                rD = HueToRgb(p, q, h + 1.0 / 3.0);
                gD = HueToRgb(p, q, h);
                bD = HueToRgb(p, q, h - 1.0 / 3.0);
            }

            r = (byte)(Math.Round(rD * 255));
            g = (byte)(Math.Round(gD * 255));
            b = (byte)(Math.Round(bD * 255));
        }

        private static double HueToRgb(double p, double q, double t)
        {
            if (t < 0) t += 1;
            if (t > 1) t -= 1;
            if (t < 1.0 / 6.0) return p + (q - p) * 6 * t;
            if (t < 1.0 / 2.0) return q;
            if (t < 2.0 / 3.0) return p + (q - p) * (2.0 / 3.0 - t) * 6;
            return p;
        }
    }
}
