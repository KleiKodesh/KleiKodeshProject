using WpfLib;

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

        public override string ToString() => Name;
    }
}
