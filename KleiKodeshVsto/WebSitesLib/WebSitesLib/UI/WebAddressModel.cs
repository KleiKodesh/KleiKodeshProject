using WpfLib.ViewModels;
namespace WebSitesLib.UI
{
    public class WebAddressModel : ViewModelBase
    {
        string _name = "";
        string _description = "";
        string _url = "";

        public string Name
        {
            get => _name;
            set => SetProperty(ref _name, value);
        }

        public string Description
        {
            get => _description;
            set => SetProperty(ref _description, value);
        }

        public string Url
        {
            get => _url;
            set => SetProperty(ref _url, value);
        }

        /// <summary>
        /// Present in the default whitelist JSON to mark entries hidden by default.
        /// The installer writes only checked entries (no IsVisible field), so this
        /// defaults to true — meaning all entries in a user-customised file are shown.
        /// When loading the default file (which ships with IsVisible fields), entries
        /// with IsVisible=false are filtered out by LoadWhitelist.
        /// </summary>
        public bool IsVisible { get; set; } = true;

        /// <summary>
        /// Display-only label: English name of a known site (matched by Url) when
        /// the UI culture is English, otherwise Name. Name/Url stay the data keys.
        /// </summary>
        public string DisplayName =>
            Properties.Strings.IsEnglish && Url != null && EnglishSiteNames.TryGetValue(Url, out var en)
                ? en : Name;

        public override string ToString() => DisplayName;

        static readonly System.Collections.Generic.Dictionary<string, string> EnglishSiteNames =
            new System.Collections.Generic.Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase)
        {
            ["https://kleikodesh.github.io/"] = "Klei Kodesh Project",
            ["https://kiwix-app/index.html"] = "Kiwix",
            ["https://dicta.org.il/tools"] = "Dicta",
            ["https://hameshiv.com"] = "HaMeshiv",
            ["https://huggingface.co/spaces/sivan22/Ituria"] = "Ituria",
            ["https://tablet.otzar.org/"] = "Otzar HaChochma",
            ["https://fjms.genizah.org/"] = "FJMS Genizah - Friedberg Project",
            ["https://m.hebrewbooks.org/sites/mobile/index.html#browsebysubject"] = "HebrewBooks",
            ["https://hebrewbooks.pages.dev"] = "Digital Bookshelf",
            ["https://www.hamichlol.org.il/"] = "HaMichlol",
            ["https://tora-forum.co.il/"] = "Torah Forum",
            ["http://www.halachabrura.org/library/library8a.htm"] = "Halacha Brura Library",
            ["https://wiki.jewishbooks.org.il/"] = "Jewish Books Wiki",
            ["http://www.toratemetfreeware.com/"] = "Torat Emet Online",
            ["https://library.alhatorah.org/"] = "AlHaTorah Library",
            ["https://www.mgketer.org/"] = "Keter Project",
            ["https://library.dicta.org.il/"] = "Dicta Library",
            ["https://shitufta.org.il/"] = "Shitufta",
            ["https://tashma.jewishoffice.co.il/"] = "Ta Shma",
            ["https://www.themercava.com/"] = "HaMercava",
            ["https://daf-yomi.com/"] = "Daf Yomi",
            ["http://www.basehasefer.com/"] = "Base HaSefer",
            ["http://www.aspaklaria.info/"] = "Aspaklaria",
            ["http://www.netivothashas.org.il/"] = "Netivot HaShas",
            ["https://www.torahanytime.com/"] = "Torah Anytime",
            ["https://www2.kolhalashon.com/#/regularSite/home"] = "Kol HaLashon",
            ["https://torazoom.com/"] = "Torah Zoom",
            ["https://www.dirshu.co.il/"] = "Dirshu",
            ["http://beinenu.com/"] = "Beinenu",
            ["https://auth.openai.com/log-in"] = "ChatGPT",
            ["https://www.yeshiva.org.il/wiki"] = "Yeshiva Wiki",
            ["https://he.wiktionary.org"] = "Wiktionary",
            ["https://www.betmidrash.org.il"] = "Wikisugia",
        };
    }
}
