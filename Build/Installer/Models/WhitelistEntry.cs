using System.ComponentModel;

namespace KleiKodeshVstoInstallerWpf
{
    /// <summary>
    /// Installer-side model for a website whitelist entry.
    /// Mirrors WebAddressModel from WebSitesLib2 — kept separate so the installer
    /// has no project reference to WebSitesLib2.
    /// Serialized/deserialized by the hand-rolled parser in ComponentSettingsPage.xaml.cs.
    /// Implements INotifyPropertyChanged so checkbox bindings update live
    /// (e.g. when Check All / Uncheck All is clicked).
    /// </summary>
    public class WhitelistEntry : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;
        private void Notify(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        private string _name        = "";
        private string _description = "";
        private string _url         = "";
        private bool   _isVisible   = true;

        public string Name
        {
            get => _name;
            set { _name = value; Notify(nameof(Name)); }
        }

        public string Description
        {
            get => _description;
            set { _description = value; Notify(nameof(Description)); }
        }

        public string Url
        {
            get => _url;
            set { _url = value; Notify(nameof(Url)); }
        }

        public bool IsVisible
        {
            get => _isVisible;
            set { _isVisible = value; Notify(nameof(IsVisible)); }
        }
    }
}
