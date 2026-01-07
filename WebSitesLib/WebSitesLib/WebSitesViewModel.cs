using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows.Media;
using WpfLib;
using WpfLib.ViewModels;

namespace WebSitesLib
{
    public class WebSitesViewModel : ViewModelBase
    {
        ObservableCollection<WebAdressModel> _adresses;
        string _mainFileName = "WebSitesWhitelist.json";
        string _settingsFileName = "UserWebSitesSettings.json";

        string AppDataFolder => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string MainJsonPath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", _mainFileName);
        string UserSettingsPath => Path.Combine(AppDataFolder, "WebWaysLib", _settingsFileName);  
        public ObservableCollection<WebAdressModel> Adresses 
        {
            get => _adresses; 
            set => SetProperty(ref _adresses, value); 
        }

        //public RelayCommand EditWhiteListCommand => new RelayCommand(() => ShowWhiteListDialog());

        public WebSitesViewModel() 
        {
            PopulateAdressList();
        }

        void PopulateAdressList()
        {
            if (File.Exists(MainJsonPath))
            {
                string json = File.ReadAllText(MainJsonPath);
                Adresses = JsonSerializer.Deserialize<ObservableCollection<WebAdressModel>>(json)
                    ?? new ObservableCollection<WebAdressModel>();
            }

            if (File.Exists(UserSettingsPath))
            {
                string userJson = File.ReadAllText(UserSettingsPath);
                var userSettings = JsonSerializer.Deserialize<Dictionary<string, bool>>(userJson) ??
                    new Dictionary<string, bool>();

                foreach (var address in Adresses)
                    if (userSettings.TryGetValue(address.Url, out bool isVisible))
                        address.IsVisible = isVisible;
            }
        }

        public void ShowWhiteListDialog(Brush background, Brush foreground)
        {
            WhiteListDialog dialog = new WhiteListDialog(this) 
            {
                Background = background,
                Foreground = foreground
            };

            dialog.ShowDialog();
            SaveUserSettings();
        }

        void SaveUserSettings()
        {
            string directoryPath = Path.GetDirectoryName(UserSettingsPath);
            if (!string.IsNullOrEmpty(directoryPath) && !Directory.Exists(directoryPath))
                Directory.CreateDirectory(directoryPath);

            var userSettings = Adresses
                .Where(address => address.IsVisible.HasValue)
                .ToDictionary(address => address.Url, address => address.IsVisible.Value);

            string userJson = JsonSerializer.Serialize(userSettings, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(UserSettingsPath, userJson);
        }

    }
}
