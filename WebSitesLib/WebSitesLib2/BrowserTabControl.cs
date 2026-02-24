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

namespace WebSitesLib2
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
                Console.WriteLine($"[BrowserTabControl] IsTabDropDownOpen → {value}");
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
                Console.WriteLine($"[BrowserTabControl] WebAddressModels set — {value?.Count ?? 0} entries");
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(WebAddressModels)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(VisibleAddressModels)));
            }
        }

        public IEnumerable<WebAddressModel> VisibleAddressModels =>
            _webAddressModels?.Where(m => m.IsVisible == true) ?? Enumerable.Empty<WebAddressModel>();

        public static ObservableCollection<BrowserTab> TabsCollection { get; }
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

        public RelayCommand NewTabCommand => new RelayCommand(() =>
        {
            Console.WriteLine("[BrowserTabControl] NewTabCommand invoked");
            AddTab();
        });

        public RelayCommand CloseTabCommand(BrowserTab browserTab) => new RelayCommand(
            () => RemoveTab(browserTab),
            () => CurrentTab != null);

        public RelayCommand CloseCurrentTabCommand() => new RelayCommand(
            () => RemoveTab(CurrentTab),
            () => CurrentTab != null);

        public RelayCommand NavigateCommand => new RelayCommand(
            () =>
            {
                Console.WriteLine($"[BrowserTabControl] NavigateCommand → '{CurrentAddressModel?.Url}'");
                CurrentTab.AddressModel = CurrentAddressModel;
            },
            () => CurrentTab != null && CurrentAddressModel != null);

        public RelayCommand EditWhiteListCommand => new RelayCommand(() =>
        {
            Console.WriteLine("[BrowserTabControl] EditWhiteListCommand — opening dialog");
            var dialog = new WhiteListDialog(_webAddressModels);
            dialog.ShowDialog();
            Console.WriteLine("[BrowserTabControl] WhiteList dialog closed — notifying & saving");
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(WebAddressModels)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(VisibleAddressModels)));
            SaveWhiteList();
        });

        /// <summary>
        /// Moves the given tab to position 0 and selects it.
        /// Bound to the dropdown list so clicking a tab there promotes it to front.
        /// </summary>
        public RelayCommand MoveToFrontCommand(BrowserTab tab) => new RelayCommand(() =>
        {
            Console.WriteLine($"[BrowserTabControl] MoveToFrontCommand — tab='{tab?.Title}'");
            if (tab == null) return;

            int index = TabsCollection.IndexOf(tab);
            if (index > 0)
            {
                Console.WriteLine($"[BrowserTabControl] Moving tab from index {index} to 0");
                TabsCollection.Move(index, 0);
            }

            SelectedItem = tab;
            Console.WriteLine($"[BrowserTabControl] Tab '{tab.Title}' is now selected at front");
        });

        #endregion

        public BrowserTabControl()
        {
            Console.WriteLine("[BrowserTabControl] Constructor start");
            ItemsSource = TabsCollection;

            LoadWhitelist();
            RestoreSessionOrCreateDefaultTab();
            HookSessionPersistence();
            Console.WriteLine("[BrowserTabControl] Constructor complete");
        }

        #region Whitelist

        string WhiteLIstPath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "WebSitesWhitelist.json");

        void LoadWhitelist()
        {
            Console.WriteLine($"[Whitelist] LoadWhitelist — path: '{WhiteLIstPath}'");
            if (!File.Exists(WhiteLIstPath))
            {
                Console.WriteLine("[Whitelist] File not found — starting with empty list");
                WebAddressModels = new ObservableCollection<WebAddressModel>();
                return;
            }

            string json = File.ReadAllText(WhiteLIstPath);
            Console.WriteLine($"[Whitelist] Raw JSON: {json}");
            WebAddressModels = JsonSerializer.Deserialize<ObservableCollection<WebAddressModel>>(json);
            Console.WriteLine($"[Whitelist] Loaded {_webAddressModels.Count} entries:");
            foreach (var m in _webAddressModels)
                Console.WriteLine($"[Whitelist]   Name='{m.Name}' Url='{m.Url}' IsVisible={m.IsVisible}");

            var visible = VisibleAddressModels.ToList();
            Console.WriteLine($"[Whitelist] Visible subset: {visible.Count} entries");
            foreach (var m in visible)
                Console.WriteLine($"[Whitelist]   (visible) Name='{m.Name}' Url='{m.Url}'");
        }

        void SaveWhiteList()
        {
            Console.WriteLine($"[Whitelist] SaveWhiteList — {_webAddressModels.Count} entries");
            string json = JsonSerializer.Serialize(_webAddressModels);
            File.WriteAllText(WhiteLIstPath, json);
            Console.WriteLine("[Whitelist] Saved");
        }

        #endregion

        #region Session Restore

        void RestoreSessionOrCreateDefaultTab()
        {
            Console.WriteLine("[Session] RestoreSessionOrCreateDefaultTab");
            if (!TryRestoreSession())
            {
                Console.WriteLine("[Session] Restore failed or no session — creating default tab");
                AddTab();
            }
        }

        bool TryRestoreSession()
        {
            Console.WriteLine($"[Session] TryRestoreSession — path: '{SessionPath}'");
            if (!File.Exists(SessionPath))
            {
                Console.WriteLine("[Session] Session file not found");
                return false;
            }

            var session = LoadSession();
            if (session?.Urls == null || session.Urls.Count == 0)
            {
                Console.WriteLine("[Session] Session file empty or invalid");
                return false;
            }

            Console.WriteLine($"[Session] Session has {session.Urls.Count} URL(s), SelectedIndex={session.SelectedIndex}:");
            foreach (var u in session.Urls)
                Console.WriteLine($"[Session]   '{u}'");

            var modelsToRestore = session.Urls
                .Select(url =>
                {
                    var match = VisibleAddressModels.FirstOrDefault(m => m.Url == url);
                    if (match != null)
                    {
                        Console.WriteLine($"[Session]   '{url}' → matched whitelist entry '{match.Name}'");
                        return match;
                    }
                    Console.WriteLine($"[Session]   '{url}' → no whitelist match, creating synthetic model");
                    return new WebAddressModel { Url = url, Name = url, IsVisible = true };
                })
                .ToList();

            Console.WriteLine($"[Session] Restoring {modelsToRestore.Count} tab(s)");
            RestoreTabs(modelsToRestore, session.SelectedIndex);
            return true;
        }

        SessionData LoadSession()
        {
            try
            {
                string json = File.ReadAllText(SessionPath);
                Console.WriteLine($"[Session] Raw session JSON: {json}");
                return JsonSerializer.Deserialize<SessionData>(json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Session] Failed to load session: {ex.Message}");
                return null;
            }
        }

        void RestoreTabs(List<WebAddressModel> models, int selectedIndex)
        {
            Console.WriteLine($"[Session] RestoreTabs — {models.Count} tab(s), selectedIndex={selectedIndex}");
            _isRestoringSession = true;

            foreach (var model in models)
            {
                Console.WriteLine($"[Session]   Adding tab for '{model.Url}'");
                TabsCollection.Add(new BrowserTab(model, this));
            }

            _isRestoringSession = false;

            if (selectedIndex >= 0 && selectedIndex < TabsCollection.Count)
            {
                Console.WriteLine($"[Session] Selecting tab at index {selectedIndex}");
                SelectedItem = TabsCollection[selectedIndex];
            }
            else
            {
                Console.WriteLine($"[Session] selectedIndex {selectedIndex} out of range — selecting tab 0");
                SelectedItem = TabsCollection[0];
            }

            var selected = (BrowserTab)SelectedItem;
            Console.WriteLine($"[Session] Selected tab AddressModel='{selected.AddressModel?.Url}'");
            UpdateAddressModel(selected.AddressModel, true);
        }

        #endregion

        #region Session Save

        void HookSessionPersistence()
        {
            Console.WriteLine("[Session] HookSessionPersistence");

            TabsCollection.CollectionChanged += (_, e) =>
            {
                Console.WriteLine($"[Session] TabsCollection changed (Action={e.Action}), _isRestoringSession={_isRestoringSession}");
                if (!_isRestoringSession)
                    SaveSession();
            };

            SelectionChanged += (_, __) =>
            {
                Console.WriteLine($"[Session] SelectionChanged — CurrentTab='{CurrentTab?.Title}', _isRestoringSession={_isRestoringSession}");
                if (!_isRestoringSession)
                {
                    SaveSession();
                    UpdateAddressModel(CurrentTab?.AddressModel);
                }
            };
        }

        void SaveSession()
        {
            Console.WriteLine("[Session] SaveSession called");

            var urls = TabsCollection
                .Select(t =>
                {
                    var live = t.CurrentUrl;
                    var model = t.AddressModel?.Url;
                    var chosen = live ?? model;
                    Console.WriteLine($"[Session]   Tab '{t.Title}': CurrentUrl='{live}' AddressModel.Url='{model}' → saving='{chosen}'");
                    return chosen;
                })
                .Where(url => url != null)
                .ToList();

            var selectedIndex = TabsCollection.IndexOf(CurrentTab);
            Console.WriteLine($"[Session] Saving {urls.Count} URL(s), SelectedIndex={selectedIndex}");

            var session = new SessionData { Urls = urls, SelectedIndex = selectedIndex };
            var json = JsonSerializer.Serialize(session);
            Console.WriteLine($"[Session] Writing: {json}");
            File.WriteAllText(SessionPath, json);
            Console.WriteLine("[Session] SaveSession complete");
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
            Console.WriteLine($"[BrowserTabControl] AddTab — first visible model='{first?.Url ?? "(none)"}'");
            if (first == null) return;

            TabsCollection.Add(new BrowserTab(first, this));
            UpdateAddressModel(first, true);
        }

        void RemoveTab(BrowserTab tab)
        {
            Console.WriteLine($"[BrowserTabControl] RemoveTab — '{tab?.Title}'");
            TabsCollection.Remove(tab);

            // Always keep at least one tab open
            if (TabsCollection.Count == 0)
            {
                Console.WriteLine("[BrowserTabControl] No tabs left — auto-opening new tab");
                AddTab();
            }
        }

        #endregion

        void UpdateAddressModel(
            WebAddressModel newModel,
            bool skipSafetyCheck = false)
        {
            Console.WriteLine($"[BrowserTabControl] UpdateAddressModel — newModel='{newModel?.Url}' " +
                              $"current='{_currentAddressModel?.Url}' skipSafetyCheck={skipSafetyCheck}");

            if (!skipSafetyCheck &&
                (newModel == _currentAddressModel || newModel == null))
            {
                Console.WriteLine("[BrowserTabControl] UpdateAddressModel — skipped (same or null)");
                return;
            }

            _currentAddressModel = newModel;
            Console.WriteLine($"[BrowserTabControl] CurrentAddressModel updated to '{newModel?.Url}'");

            PropertyChanged?.Invoke(
                this,
                new PropertyChangedEventArgs(nameof(CurrentAddressModel)));

            if (CurrentTab != null &&
                (skipSafetyCheck || IsTabDropDownOpen))
            {
                Console.WriteLine($"[BrowserTabControl] Pushing AddressModel to CurrentTab '{CurrentTab.Title}'");
                CurrentTab.AddressModel = newModel;
            }
            else
            {
                Console.WriteLine($"[BrowserTabControl] Not pushing to tab — " +
                                  $"CurrentTab='{CurrentTab?.Title ?? "(null)"}' IsTabDropDownOpen={IsTabDropDownOpen}");
            }
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
                if (value == null)
                {
                    Console.WriteLine($"[BrowserTab '{Title}'] AddressModel set to null — ignored");
                    return;
                }

                Console.WriteLine($"[BrowserTab '{Title}'] AddressModel → '{value.Url}' (was '{_addressModel?.Url}') _isLoaded={_isLoaded}");
                _addressModel = value;

                if (_isLoaded)
                    NavigateTo(value.Url);

                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AddressModel)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Title)));
            }
        }

        public string Title =>
            _addressModel?.Name ?? "כרטיסייה חדשה";

        /// <summary>
        /// Live URL from the WebView, updated on every NavigationCompleted.
        /// Used by SaveSession so actual navigation (links, redirects, back/forward)
        /// is persisted rather than just the last combobox selection.
        /// </summary>
        public string CurrentUrl { get; private set; }

        public RelayCommand CloseTabCommand { get; }

        public MyWebView WebView { get; private set; }

        readonly WindowsFormsHost _host;

        public BrowserTab(
            WebAddressModel webAddressModel,
            BrowserTabControl owner)
        {
            Console.WriteLine($"[BrowserTab] Constructor — model='{webAddressModel?.Url}'");

            CloseTabCommand = new RelayCommand(() =>
            {
                Console.WriteLine($"[BrowserTab '{Title}'] CloseTabCommand invoked");
                BrowserTabControl.TabsCollection.Remove(this);
            });

            // Expose MoveToFrontCommand so the dropdown item can call it via DataContext binding
            MoveToFrontCommand = owner.MoveToFrontCommand(this);

            WebView = new MyWebView();

            // Update CurrentUrl on every navigation so SaveSession always
            // records the live page rather than the original whitelist URL.
            WebView.NavigationCompleted += (s, e) =>
            {
                var url = WebView.Source?.ToString();
                Console.WriteLine($"[BrowserTab '{Title}'] NavigationCompleted — Source='{url}' IsSuccess={e.IsSuccess}");
                CurrentUrl = url;
            };

            _host = new WindowsFormsHost();
            _host.Child = WebView;

            Content = _host;
            IsSelected = true;

            _addressModel = webAddressModel;

            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AddressModel)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Title)));

            _host.IsVisibleChanged += OnHostVisibleChanged;
        }

        /// <summary>
        /// Moves this tab to the front of the tab strip and selects it.
        /// Bound in the dropdown popup so clicking a tab there promotes it.
        /// </summary>
        public RelayCommand MoveToFrontCommand { get; }

        void OnHostVisibleChanged(
            object s,
            System.Windows.DependencyPropertyChangedEventArgs e)
        {
            Console.WriteLine(
                $"[BrowserTab '{Title}'] Host IsVisibleChanged — NewValue={e.NewValue} _addressModel='{_addressModel?.Url}'");

            if (!(bool)e.NewValue) return;

            _host.IsVisibleChanged -= OnHostVisibleChanged;

            _isLoaded = true;
            Console.WriteLine($"[BrowserTab '{Title}'] _isLoaded = true, navigating to '{_addressModel?.Url}'");

            NavigateTo(_addressModel.Url);
        }

        void NavigateTo(string url)
        {
            Console.WriteLine($"[BrowserTab '{Title}'] NavigateTo '{url}' _isLoaded={_isLoaded}");

            _ = WebView.Navigate(url).ContinueWith(t =>
            {
                if (t.IsFaulted)
                    Console.WriteLine($"[BrowserTab '{Title}'] Navigate() task FAULTED: {t.Exception?.GetBaseException().Message}");
                else
                    Console.WriteLine($"[BrowserTab '{Title}'] Navigate() task completed for '{url}'");
            }, TaskScheduler.Default);
        }
    }
}