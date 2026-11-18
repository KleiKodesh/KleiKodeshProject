using KiwixLib;
using System;
using System.Windows.Forms;

namespace KiwixDemoApp
{
    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            // "--culture he-IL" / "--culture en-US": the UI language, as the add-in sets it in Word.
            var at = Array.IndexOf(args, "--culture");
            if (at >= 0 && at + 1 < args.Length)
            {
                var culture = new System.Globalization.CultureInfo(args[at + 1]);
                System.Globalization.CultureInfo.DefaultThreadCurrentUICulture = culture;
                System.Threading.Thread.CurrentThread.CurrentUICulture = culture;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            // "--theme office-black" etc.: the Office theme, as the add-in applies it to the pane.
            var themeAt = Array.IndexOf(args, "--theme");
            string theme = themeAt >= 0 && themeAt + 1 < args.Length ? args[themeAt + 1] : null;

            Application.Run(new MainForm(theme));
        }
    }
}