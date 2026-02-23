using WpfLib;

namespace WebSitesLib2
{
    public class WebAddressModel : ViewModelBase
    {
        private string _name = "";
        private string _description = "";
        private string _url = "";
        private bool _isVisible = true;

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

        public bool IsVisible
        {
            get => _isVisible;
            set => SetProperty(ref _isVisible, value);
        }

        public override string ToString() => Name;
    }
}