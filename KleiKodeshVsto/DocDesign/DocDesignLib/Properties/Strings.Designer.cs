//------------------------------------------------------------------------------
// Hand-maintained (mirrors PublicResXFileCodeGenerator output). Keep in sync with Strings.resx.
//------------------------------------------------------------------------------
namespace DocDesign.Properties
{
    using System.Globalization;
    using System.Resources;

    public class Strings
    {
        static ResourceManager resourceMan;
        static CultureInfo resourceCulture;

        internal Strings() { }

        /// <summary>Reads English from the embedded "DocDesign.Properties.Strings.en.resources" (no satellite dll).</summary>
        public static ResourceManager ResourceManager =>
            resourceMan ?? (resourceMan = new EmbeddedCultureResourceManager());

        public static CultureInfo Culture { get => resourceCulture; set => resourceCulture = value; }

        public static string SectionParagraph => ResourceManager.GetString("SectionParagraph", resourceCulture);
        public static string MinLineCount => ResourceManager.GetString("MinLineCount", resourceCulture);
        public static string TipFirstWordStyle => ResourceManager.GetString("TipFirstWordStyle", resourceCulture);
        public static string TipRemoveFirstWordStyle => ResourceManager.GetString("TipRemoveFirstWordStyle", resourceCulture);
        public static string TipHanging => ResourceManager.GetString("TipHanging", resourceCulture);
        public static string TipDoubleHanging => ResourceManager.GetString("TipDoubleHanging", resourceCulture);
        public static string TipRemoveHanging => ResourceManager.GetString("TipRemoveHanging", resourceCulture);
        public static string TipCenterLastLine => ResourceManager.GetString("TipCenterLastLine", resourceCulture);
        public static string TipRemoveCenterLastLine => ResourceManager.GetString("TipRemoveCenterLastLine", resourceCulture);
        public static string ApplyTo => ResourceManager.GetString("ApplyTo", resourceCulture);
        public static string SelectAll => ResourceManager.GetString("SelectAll", resourceCulture);
        public static string ChooseFirstWordStyle => ResourceManager.GetString("ChooseFirstWordStyle", resourceCulture);
        public static string TipChooseFirstWordStyle => ResourceManager.GetString("TipChooseFirstWordStyle", resourceCulture);
        public static string TipNewCharStyle => ResourceManager.GetString("TipNewCharStyle", resourceCulture);
        public static string NewStyle => ResourceManager.GetString("NewStyle", resourceCulture);
        public static string SectionColumns => ResourceManager.GetString("SectionColumns", resourceCulture);
        public static string TipAlignColumns => ResourceManager.GetString("TipAlignColumns", resourceCulture);
        public static string TipColumnsSettings => ResourceManager.GetString("TipColumnsSettings", resourceCulture);
        public static string TipFindUneven => ResourceManager.GetString("TipFindUneven", resourceCulture);
        public static string MaxSpaceBetween => ResourceManager.GetString("MaxSpaceBetween", resourceCulture);
        public static string SectionSpacing => ResourceManager.GetString("SectionSpacing", resourceCulture);
        public static string SpaceAfter => ResourceManager.GetString("SpaceAfter", resourceCulture);
        public static string SpaceBefore => ResourceManager.GetString("SpaceBefore", resourceCulture);
        public static string LineSpacing => ResourceManager.GetString("LineSpacing", resourceCulture);
        public static string WordSpacing => ResourceManager.GetString("WordSpacing", resourceCulture);
        public static string CharStretch => ResourceManager.GetString("CharStretch", resourceCulture);
        public static string CharScale => ResourceManager.GetString("CharScale", resourceCulture);
        public static string StepSize => ResourceManager.GetString("StepSize", resourceCulture);
        public static string TipReset => ResourceManager.GetString("TipReset", resourceCulture);
        public static string TipIncrease => ResourceManager.GetString("TipIncrease", resourceCulture);
        public static string TipDecrease => ResourceManager.GetString("TipDecrease", resourceCulture);
        public static string NoResults => ResourceManager.GetString("NoResults", resourceCulture);
        public static string NoResultsSearchFromStart => ResourceManager.GetString("NoResultsSearchFromStart", resourceCulture);
        public static string UndoAlignColumns => ResourceManager.GetString("UndoAlignColumns", resourceCulture);
        public static string StatusDone => ResourceManager.GetString("StatusDone", resourceCulture);
        public static string UndoResetTabs => ResourceManager.GetString("UndoResetTabs", resourceCulture);
        public static string UndoCenterLastLine => ResourceManager.GetString("UndoCenterLastLine", resourceCulture);
        public static string UndoHanging => ResourceManager.GetString("UndoHanging", resourceCulture);
        public static string UndoFirstWordStyle => ResourceManager.GetString("UndoFirstWordStyle", resourceCulture);
        public static string UndoRemoveFirstWordStyle => ResourceManager.GetString("UndoRemoveFirstWordStyle", resourceCulture);
        public static string NewStylePrompt => ResourceManager.GetString("NewStylePrompt", resourceCulture);
        public static string NewStyleTitle => ResourceManager.GetString("NewStyleTitle", resourceCulture);
        public static string StyleExistsFormat => ResourceManager.GetString("StyleExistsFormat", resourceCulture);
        public static string StyleCreatedFormat => ResourceManager.GetString("StyleCreatedFormat", resourceCulture);
        public static string StyleCreatedTitle => ResourceManager.GetString("StyleCreatedTitle", resourceCulture);
    }
}
