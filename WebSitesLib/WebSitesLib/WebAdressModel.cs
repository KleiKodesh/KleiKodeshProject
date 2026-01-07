using WpfLib;
using WpfLib.Helpers;

namespace WebSitesLib
{
    public class WebAdressModel : ViewModelBase
    {
        string _name;
        string _description;
        bool _isVisible;

        public string Name { get => _name; set => _name = value; }
        public string Description { get => _description; set => _description = value; }
        public string Url { get; set; }
        public bool? IsVisible 
        {
            get => _isVisible;
            set
            {
                if (value == null) value = false;
                SetProperty(ref _isVisible, (bool)value);
            }
        }

        public override string ToString() => Name;
    }
}
