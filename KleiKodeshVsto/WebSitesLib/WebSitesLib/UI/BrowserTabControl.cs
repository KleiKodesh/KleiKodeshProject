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
        bool _isAddressDropDownOpen;
        bool _isRestoringSession;

        static readonly string SessionPath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "TabSession.json");

        public WebAddressModel CurrentAddressModel
        {
            get => _currentAddressModel;
            set => UpdateAddressModel(value);
        }

        public bool IsAddressDropDownOpen
        {
            get => _isAddressDropDownOpen;
            set
            {
                _isAddressDropDownOpen = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsAddressDropDownOpen)));
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
            }
        }



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

        public RelayCommand MoveToFrontCommand(BrowserTab tab) => new RelayCommand(() =>
        {
            if (tab == null) return;
            int index = TabsCollection.IndexOf(tab);
            if (index > 0) TabsCollection.Move(index, 0);
            SelectedItem = tab;
        });

        public RelayCommand NavigateToTabCommand(BrowserTab tab) => new RelayCommand(() =>
        {
            if (tab == null) return;
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
            WebAddressModels = JsonSerializer.Deserialize<ObservableCollection<WebAddressModel>>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
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
                    var match = WebAddressModels
                        .FirstOrDefault(m => url.StartsWith(m.Url, StringComparison.OrdinalIgnoreCase));
                    return (model: match ?? new WebAddressModel { Url = url, Name = url },
                            actualUrl: url);
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

        void RestoreTabs(List<(WebAddressModel model, string actualUrl)> entries, int selectedIndex)
        {
            _isRestoringSession = true;
            foreach (var (model, actualUrl) in entries)
                TabsCollection.Add(new BrowserTab(model, actualUrl, this));
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
            var first = WebAddressModels?.FirstOrDefault();
            if (first == null) return;
            TabsCollection.Add(new BrowserTab(first, this));
            UpdateAddressModel(first, true);
        }

        internal void RemoveTab(BrowserTab tab)
        {
            int index = TabsCollection.IndexOf(tab);
            TabsCollection.Remove(tab);
            if (TabsCollection.Count == 0)
            {
                AddTab();
            }
            else
            {
                int selectIndex = Math.Min(index, TabsCollection.Count - 1);
                SelectedItem = TabsCollection[selectIndex];
            }
        }

        #endregion

        void UpdateAddressModel(WebAddressModel newModel, bool force = false)
        {
            if (!force && (newModel == _currentAddressModel || newModel == null)) return;
            _currentAddressModel = newModel;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentAddressModel)));
            if (CurrentTab != null && (force || IsAddressDropDownOpen))
                CurrentTab.AddressModel = newModel;
        }

        internal void OnTabNavigationCompleted(BrowserTab tab)
        {
            SaveSession();

            // Only update the combobox if this is the currently visible tab
            if (tab != CurrentTab) return;

            // Find the whitelist entry whose Url is a prefix of the actual navigated URL
            var actualUrl = tab.CurrentUrl;
            if (string.IsNullOrEmpty(actualUrl)) return;

            var match = WebAddressModels
                .FirstOrDefault(m => actualUrl.StartsWith(m.Url, StringComparison.OrdinalIgnoreCase));

            if (match != null && match != _currentAddressModel)
            {
                _currentAddressModel = match;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentAddressModel)));
            }
        }
    }

    internal class BrowserTab : TabItem, INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        WebAddressModel _addressModel;
        string _navigateUrl;
        bool _isLoaded;

        public WebAddressModel AddressModel
        {
            get => _addressModel;
            set
            {
                if (value == null) return;
                _addressModel = value;
                _navigateUrl = value.Url;
                if (_isLoaded) NavigateTo(_navigateUrl);
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AddressModel)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Title)));
            }
        }

        public string Title => _addressModel?.Name ?? "כרטיסייה חדשה";
        public string CurrentUrl { get; private set; }

        public RelayCommand CloseTabCommand { get; }
        public RelayCommand MoveToFrontCommand { get; }
        public RelayCommand NavigateToTabCommand { get; }
        public MyWebView WebView { get; }

        readonly WindowsFormsHost _host;

        public BrowserTab(WebAddressModel model, BrowserTabControl owner)
            : this(model, null, owner) { }

        public BrowserTab(WebAddressModel model, string actualUrl, BrowserTabControl owner)
        {
            CloseTabCommand      = new RelayCommand(() => owner.RemoveTab(this));
            MoveToFrontCommand   = owner.MoveToFrontCommand(this);
            NavigateToTabCommand = owner.NavigateToTabCommand(this);

            _navigateUrl = actualUrl ?? model.Url;

            WebView = new MyWebView();
            WebView.NavigationCompleted += (s, e) =>
            {
                CurrentUrl = WebView.Source?.ToString();
                owner.OnTabNavigationCompleted(this);
            };

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
            NavigateTo(_navigateUrl);
        }

        void NavigateTo(string url)
        {
            _ = WebView.Navigate(url).ContinueWith(t => { }, TaskScheduler.Default);
        }
    }
}
