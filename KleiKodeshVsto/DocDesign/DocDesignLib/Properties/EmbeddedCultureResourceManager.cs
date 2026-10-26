using System.Globalization;
using System.Resources;
using System.Windows;

namespace DocDesign.Properties
{
    /// <summary>
    /// Serves Strings texts. Hebrew (Strings.resx) is the neutral default; English
    /// (Strings.en.resx) is embedded in this assembly as "DocDesign.Properties.Strings.en.resources"
    /// instead of an en\ satellite dll. The host picks the language by setting
    /// Thread.CurrentUICulture before constructing the pane.
    /// </summary>
    sealed class EmbeddedCultureResourceManager : ResourceManager
    {
        const string StringsBaseName = "DocDesign.Properties.Strings";

        public EmbeddedCultureResourceManager() : base(StringsBaseName, typeof(Strings).Assembly) { }

        protected override ResourceSet InternalGetResourceSet(CultureInfo culture, bool createIfNotExists, bool tryParents)
        {
            if (!culture.Equals(CultureInfo.InvariantCulture))
            {
                var stream = MainAssembly.GetManifestResourceStream(
                    StringsBaseName + "." + culture.TwoLetterISOLanguageName + ".resources");
                if (stream != null) return new ResourceSet(stream);
            }
            return base.InternalGetResourceSet(culture, createIfNotExists, tryParents);
        }
    }

    /// <summary>Layout direction derived from the current UI culture.</summary>
    public static class UiDirection
    {
        public static bool IsRightToLeft => CultureInfo.CurrentUICulture.TextInfo.IsRightToLeft;

        public static FlowDirection FlowDirection =>
            IsRightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;

        public static System.Windows.Forms.MessageBoxOptions WinFormsMessageBoxOptions =>
            IsRightToLeft
                ? System.Windows.Forms.MessageBoxOptions.RightAlign | System.Windows.Forms.MessageBoxOptions.RtlReading
                : 0;
    }
}
