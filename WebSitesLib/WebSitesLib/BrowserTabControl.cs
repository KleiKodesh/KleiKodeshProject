using Microsoft.VisualBasic;
using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using WebViewLib;
using WpfLib.ViewModels;

namespace WebSitesLib
{
    public class BrowserTabControl : TabControl
    {
        // ── Address dependency property ──────────────────────────────────────
        public static readonly DependencyProperty AddressProperty =
            DependencyProperty.Register(
                nameof(Address),
                typeof(string),
                typeof(BrowserTabControl),
                new FrameworkPropertyMetadata(
                    null,
                    FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                    OnAddressChanged));

        public string Address
        {
            get => (string)GetValue(AddressProperty);
            set => SetValue(AddressProperty, value);
        }

        // ── Commands ─────────────────────────────────────────────────────────
        public ICommand AddTabCommand { get; }
        public ICommand CloseTabCommand { get; }
        public ICommand GoBackCommand { get; }
        public ICommand GoForwardCommand { get; }
        public ICommand ReloadCommand { get; }
        public ICommand PromoteTabCommand { get; }

        // ── Constructor ──────────────────────────────────────────────────────
        public BrowserTabControl()
        {
            AddTabCommand = new RelayCommand(() => AddTab());

            CloseTabCommand = new RelayCommand<BrowserTabItem>(tab =>
            {
                if (tab == null) return;
                tab.PageNavigated -= OnTabPageNavigated;
                Items.Remove(tab);
            });

            GoBackCommand = new RelayCommand(
                () => CurrentWebView().GoBack(),
                () => CurrentWebView() != null && CurrentWebView().CanGoBack);

            GoForwardCommand = new RelayCommand(
                () => CurrentWebView().GoForward(),
                () => CurrentWebView() != null && CurrentWebView().CanGoForward);

            ReloadCommand = new RelayCommand(
                () => CurrentWebView().CoreWebView2.Reload(),
                () => CurrentWebView() != null && CurrentWebView().CoreWebView2 != null);

            PromoteTabCommand = new RelayCommand<BrowserTabItem>(tab =>
            {
                if (tab == null || !Items.Contains(tab)) return;
                tab.PageNavigated -= OnTabPageNavigated;
                Items.Remove(tab);
                Items.Insert(0, tab);
                tab.PageNavigated += OnTabPageNavigated;
                SelectedItem = tab;
            });

            Loaded += OnLoaded;
        }

        // ── Startup ──────────────────────────────────────────────────────────
        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (Items.Count > 0) return;

            string lastPage = Interaction.GetSetting(
                "KleiKodesh",
                "History",
                "LastPage",
                "https://kleikodesh.github.io/");

            AddTab(string.IsNullOrWhiteSpace(lastPage) ? "https://kleikodesh.github.io/" : lastPage);
        }

        // ── Helpers ──────────────────────────────────────────────────────────
        private Microsoft.Web.WebView2.WinForms.WebView2 CurrentWebView()
        {
            var tab = SelectedItem as BrowserTabItem;
            return tab?.WebViewHost?.WebView;
        }

        private static void OnAddressChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var control = (BrowserTabControl)d;
            var tab = control.SelectedItem as BrowserTabItem;
            var url = e.NewValue as string;

            if (tab != null && !string.IsNullOrWhiteSpace(url))
                tab.Navigate(url);
        }

        protected override void OnSelectionChanged(SelectionChangedEventArgs e)
        {
            base.OnSelectionChanged(e);
            if (SelectedItem is BrowserTabItem tab)
                Address = tab.Url;
        }

        // ── Tab management ───────────────────────────────────────────────────
        public void AddTab(string url = null)
        {
            var tab = new BrowserTabItem();
            tab.PageNavigated += OnTabPageNavigated;
            Items.Add(tab);
            SelectedItem = tab;
            tab.Initialize();

            if (!string.IsNullOrWhiteSpace(url))
                tab.Navigate(url);
        }

        private void OnTabPageNavigated(string url)
        {
            Interaction.SaveSetting(
                "KleiKodesh",
                "History",
                "LastPage",
                url);
        }
    }

    // ═════════════════════════════════════════════════════════════════════
    public class BrowserTabItem : TabItem, INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;
        private void Notify(string p) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(p));

        public WebViewHost WebViewHost { get; }
        public string Url { get; private set; }

        private string _title = "כרטיסייה חדשה";
        public string Title
        {
            get => _title;
            private set
            {
                _title = value;
                Notify(nameof(Title));
            }
        }

        public event Action<string> PageNavigated;

        private bool _initialized;

        public BrowserTabItem()
        {
            Header = null;
            Padding = new Thickness(0);
            WebViewHost = new WebViewHost();
            Content = WebViewHost;

            Loaded += async (s, e) =>
            {
                if (!_initialized)
                {
                    await WebViewHost.EnsurCoreAsync();
                    HookEvents();
                    _initialized = true;
                }
            };
        }

        public async void Initialize()
        {
            if (_initialized) return;
            await WebViewHost.EnsurCoreAsync();
            HookEvents();
            _initialized = true;
        }

        private void HookEvents()
        {
            WebViewHost.WebView.NavigationCompleted += (s, e) =>
            {
                if (WebViewHost.Source == null) return;

                Url = WebViewHost.Source;
                PageNavigated?.Invoke(Url);

                string docTitle = WebViewHost.WebView.CoreWebView2?.DocumentTitle;
                Title = string.IsNullOrWhiteSpace(docTitle) ? "כרטיסייה" : docTitle;
            };
        }

        public void Navigate(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return;
            Url = url;
            WebViewHost.Source = url;
        }
    }
}