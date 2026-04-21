using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Forms.Integration;
using WpfLib.ViewModels;

namespace WebSitesLib.UI
{
    internal class BrowserTabControl : TabControl, INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        WebAddressModel _currentAddressModel;
        bool _isTabDropDownOpen;
        bool _isRestoringSession;

        static readonly string SessionPath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "TabSession.json");

        public WebAddressModel CurrentAddressModel
        {
            get => _currentAddressModel;
            set => UpdateAddressModel(value);
        }

        public bool IsTabDropDownOpen
        {
            get => _isTabDropDownOpen;
            set
            {
                _isTabDropDownOpen = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsTabDropDownOpen)));
            }
        }

        ObservableCollection<WebAddressModel> _webAddressModels;
        public ObservableCollection<WebAddressModel> WebAddressModels
        {
            get => _webAddressModels;
            private set
            {
                _webAddressModels = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(WebAddressModels)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(VisibleAddressModels)));
            }
        }

        public IEnumerable<WebAddressModel> VisibleAddressModels =>
            _webAddressModels?.Where(m => m.IsVisible == true) ?? Enumerable.Empty<WebAddressModel>();

        public ObservableCollection<BrowserTab> TabsCollection { get; }
            = new ObservableCollection<BrowserTab>();

        BrowserTab CurrentTab => (BrowserTab)SelectedItem;

        #region Commands

        public RelayCommand GoBackCommand => new RelayCommand(
            () => CurrentTab.WebView.GoBack(),
            () => CurrentTab != null && CurrentTab.WebView.CanGoBack);

        public RelayCommand GoForwardCommand => new RelayCommand(
            () => CurrentTab.WebView.GoForward(),
            () => CurrentTab != null && CurrentTab.WebView.CanGoForward);

        public RelayCommand RefreshCommand => new RelayCommand(
            () => CurrentTab.WebView.Reload(),
            () => CurrentTab != null);

        public RelayCommand NewTabCommand => new RelayCommand(AddTab);

        public RelayCommand CloseTabCommand(BrowserTab tab) => new RelayCommand(
            () => RemoveTab(tab),
            () => CurrentTab != null);

        public RelayCommand NavigateCommand => new RelayCommand(
            () => { CurrentTab.AddressModel = CurrentAddressModel; },
            () => CurrentTab != null && CurrentAddressModel != null);

        public RelayCommand EditWhiteListCommand => new RelayCommand(() =>
        {
            var dialog = new WhiteListDialog(_webAddressModels);
            dialog.ShowDialog();
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(WebAddressModels)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(VisibleAddressModels)));
            SaveWhiteList();
        });

        public RelayCommand MoveToFrontCommand(BrowserTab tab) => new RelayCommand(() =>
        {
            if (tab == null) return;
            int index = TabsCollection.IndexOf(tab);
            if (index > 0) TabsCollection.Move(index, 0);
            SelectedItem = tab;
        });

        #endregion

        public BrowserTabControl()
        {
            ItemsSource = TabsCollection;
            LoadWhitelist();
            RestoreSessionOrCreateDefaultTab();
            HookSessionPersistence();
        }

        #region Whitelist

        string WhitelistPath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "WebSitesWhitelist.json");

        void LoadWhitelist()
        {
            if (!File.Exists(WhitelistPath))
            {
                WebAddressModels = new ObservableCollection<WebAddressModel>();
                return;
            }
            string json = File.ReadAllText(WhitelistPath);
            WebAddressModels = JsonSerializer.Deserialize<ObservableCollection<WebAddressModel>>(json);
        }

        void SaveWhiteList()
        {
            string json = JsonSerializer.Serialize(_webAddressModels);
            File.WriteAllText(WhitelistPath, json);
        }

        #endregion

        #region Session

        void RestoreSessionOrCreateDefaultTab()
        {
            if (!TryRestoreSession()) AddTab();
        }

        bool TryRestoreSession()
        {
            if (!File.Exists(SessionPath)) return false;
            var session = LoadSession();
            if (session?.Urls == null || session.Urls.Count == 0) return false;

            var models = session.Urls
                .Select(url =>
                {
                    var match = VisibleAddressModels.FirstOrDefault(m => m.Url == url);
                    return match ?? new WebAddressModel { Url = url, Name = url, IsVisible = true };
                })
                .ToList();

            RestoreTabs(models, session.SelectedIndex);
            return true;
        }

        SessionData LoadSession()
        {
            try { return JsonSerializer.Deserialize<SessionData>(File.ReadAllText(SessionPath)); }
            catch { return null; }
        }

        void RestoreTabs(List<WebAddressModel> models, int selectedIndex)
        {
            _isRestoringSession = true;
            foreach (var model in models)
                TabsCollection.Add(new BrowserTab(model, this));
            _isRestoringSession = false;

            int idx = (selectedIndex >= 0 && selectedIndex < TabsCollection.Count) ? selectedIndex : 0;
            SelectedItem = TabsCollection[idx];
            UpdateAddressModel(((BrowserTab)SelectedItem).AddressModel, true);
        }

        void HookSessionPersistence()
        {
            TabsCollection.CollectionChanged += (_, e) =>
            {
                if (!_isRestoringSession) SaveSession();
            };
            SelectionChanged += (_, __) =>
            {
                if (!_isRestoringSession)
                {
                    SaveSession();
                    UpdateAddressModel(CurrentTab?.AddressModel);
                }
            };
        }

        void SaveSession()
        {
            var urls = TabsCollection
                .Select(t => t.CurrentUrl ?? t.AddressModel?.Url)
                .Where(u => u != null)
                .ToList();
            var session = new SessionData { Urls = urls, SelectedIndex = TabsCollection.IndexOf(CurrentTab) };
            File.WriteAllText(SessionPath, JsonSerializer.Serialize(session));
        }

        class SessionData
        {
            public List<string> Urls { get; set; }
            public int SelectedIndex { get; set; }
        }

        #endregion

        #region Tabs

        void AddTab()
        {
            var first = VisibleAddressModels.FirstOrDefault();
            if (first == null) return;
            TabsCollection.Add(new BrowserTab(first, this));
            UpdateAddressModel(first, true);
        }

        void RemoveTab(BrowserTab tab)
        {
            TabsCollection.Remove(tab);
            if (TabsCollection.Count == 0) AddTab();
        }

        #endregion

        void UpdateAddressModel(WebAddressModel newModel, bool force = false)
        {
            if (!force && (newModel == _currentAddressModel || newModel == null)) return;
            _currentAddressModel = newModel;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentAddressModel)));
            if (CurrentTab != null && (force || IsTabDropDownOpen))
                CurrentTab.AddressModel = newModel;
        }
    }

    internal class BrowserTab : TabItem, INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        WebAddressModel _addressModel;
        bool _isLoaded;

        public WebAddressModel AddressModel
        {
            get => _addressModel;
            set
            {
                if (value == null) return;
                _addressModel = value;
                if (_isLoaded) NavigateTo(value.Url);
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AddressModel)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Title)));
            }
        }

        public string Title => _addressModel?.Name ?? "כרטיסייה חדשה";
        public string CurrentUrl { get; private set; }

        public RelayCommand CloseTabCommand { get; }
        public RelayCommand MoveToFrontCommand { get; }
        public MyWebView WebView { get; }

        readonly WindowsFormsHost _host;

        public BrowserTab(WebAddressModel model, BrowserTabControl owner)
        {
            CloseTabCommand    = new RelayCommand(() => owner.TabsCollection.Remove(this));
            MoveToFrontCommand = owner.MoveToFrontCommand(this);

            WebView = new MyWebView();
            WebView.NavigationCompleted += (s, e) => CurrentUrl = WebView.Source?.ToString();

            _host = new WindowsFormsHost { Child = WebView };
            Content = _host;
            IsSelected = true;

            _addressModel = model;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AddressModel)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Title)));

            _host.IsVisibleChanged += OnHostVisibleChanged;
        }

        void OnHostVisibleChanged(object s, System.Windows.DependencyPropertyChangedEventArgs e)
        {
            if (!(bool)e.NewValue) return;
            _host.IsVisibleChanged -= OnHostVisibleChanged;
            _isLoaded = true;
            NavigateTo(_addressModel.Url);
        }

        void NavigateTo(string url)
        {
            _ = WebView.Navigate(url).ContinueWith(t => { }, TaskScheduler.Default);
        }
    }
}
