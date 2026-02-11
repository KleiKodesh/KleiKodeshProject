using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Forms;
using Zayit.Services;

namespace Zayit.Viewer
{
    public class ZayitViewer : WebView2
    {
        private static CoreWebView2Environment _sharedEnvironment;
        private static readonly object _envLock = new object();
        private static int _instanceCounter = 0;
        private readonly int _instanceId;

        private ServiceProvider _services;
        private WebViewBridgeService _bridge;
        private readonly string HtmlPath;

        private bool _coreInitialized;

        public ZayitViewer(object commandHandler = null)
        {
            _instanceId = ++_instanceCounter;
            Console.WriteLine($"[ZayitViewer] Creating instance #{_instanceId}");

            // Get Html path - handle both regular and ClickOnce deployments
            HtmlPath = GetHtmlPath();
            Console.WriteLine($"[ZayitViewer#{_instanceId}] Html path: {HtmlPath}");

            // Ensure crisp rendering on high-DPI displays
            this.Dock = DockStyle.Fill;

            // Set WebView2 specific properties for crisp rendering
            this.DefaultBackgroundColor = System.Drawing.Color.White;

            // Initialize services directly
            InitializeServices();

            // Wire initialization event
            this.CoreWebView2InitializationCompleted += ZayitViewer_CoreWebView2InitializationCompleted;

            // Fire-and-forget async safely
            _ = EnsureCoreAsyncSafe();
        }

        private void InitializeServices()
        {
            try
            {
                Console.WriteLine($"[ZayitViewer#{_instanceId}] Initializing services...");

                var dbQueries = new DbQueries();
                _services = new ServiceProvider(this, dbQueries);
                _bridge = new WebViewBridgeService(this, _services);

                Console.WriteLine($"[ZayitViewer#{_instanceId}] Services initialized successfully");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ZayitViewer#{_instanceId}] Error initializing services: {ex}");
                throw; // Re-throw to prevent app from continuing in broken state
            }
        }

        public void SetCommandHandler(object commandHandler)
        {
            // Legacy method - services are now initialized directly
            // This method is kept for backward compatibility but does nothing
        }

        public void SetPopOutToggleAction(Action popOutToggleAction)
        {
            _services?.SetPopOutToggleAction(popOutToggleAction);
        }

        public async Task EnsureCoreAsyncSafe()
        {
            try
            {
                var environment = await GetSharedEnvironmentAsync();
                await EnsureCoreWebView2Async(environment);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("WebView2 initialization failed: " + ex);
            }
        }


        private static async Task<CoreWebView2Environment> GetSharedEnvironmentAsync()
        {
            if (_sharedEnvironment != null)
                return _sharedEnvironment;

            lock (_envLock)
            {
                if (_sharedEnvironment != null)
                    return _sharedEnvironment; // double-check after lock
            }

            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "zayit-vue-app",
                                       "ZayitWebView2SharedCache");

            // Create environment with options for crisp rendering
            var options = new CoreWebView2EnvironmentOptions();

            // Add command line arguments for better rendering quality
            options.AdditionalBrowserArguments =
                "--disable-web-security " +
                "--disable-features=VizDisplayCompositor " +
                "--enable-gpu-rasterization " +
                "--enable-zero-copy " +
                "--enable-hardware-overlays";

            var env = await CoreWebView2Environment.CreateAsync(
                browserExecutableFolder: null,
                userDataFolder: path,
                options: options);

            lock (_envLock)
            {
                _sharedEnvironment = env; // save for all instances
            }

            return _sharedEnvironment;
        }

        private static string GetHtmlPath()
        {
            // Try multiple paths to handle different deployment scenarios
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;

            // 1. Standard deployment: Html folder in base directory
            string standardPath = Path.Combine(baseDir, "zayit-vue-app");
            if (Directory.Exists(standardPath) && File.Exists(Path.Combine(standardPath, "index.html")))
            {
                Console.WriteLine($"[ZayitViewer] Using standard Html path: {standardPath}");
                return standardPath;
            }

            // 2. ClickOnce deployment: Check assembly location
            string assemblyPath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            string clickOncePath = Path.Combine(assemblyPath, "zayit-vue-app");
            if (Directory.Exists(clickOncePath) && File.Exists(Path.Combine(clickOncePath, "index.html")))
            {
                Console.WriteLine($"[ZayitViewer] Using ClickOnce Html path: {clickOncePath}");
                return clickOncePath;
            }

            // 3. Fallback: Return standard path even if it doesn't exist (will fail later with clear error)
            MessageBox.Show($"[ZayitViewer] WARNING: Html folder not found! Tried: {standardPath} and {clickOncePath}");
            Console.WriteLine($"[ZayitViewer] Falling back to standard path: {standardPath}");
            return standardPath;
        }


        private void ZayitViewer_CoreWebView2InitializationCompleted(object sender, CoreWebView2InitializationCompletedEventArgs e)
        {
            Console.WriteLine($"[ZayitViewer#{_instanceId}] CoreWebView2InitializationCompleted called, _coreInitialized: {_coreInitialized}");

            if (_coreInitialized)
            {
                Console.WriteLine($"[ZayitViewer#{_instanceId}] Already initialized, returning");
                return; // prevent double initialization
            }
            _coreInitialized = true;

            if (!e.IsSuccess)
            {
                Console.WriteLine($"[ZayitViewer#{_instanceId}] WebView2 failed to initialize: " + e.InitializationException);
                return;
            }

            try
            {
                Console.WriteLine($"[ZayitViewer#{_instanceId}] Setting up WebView2...");

                // Configure WebView2 settings for crisp rendering
                var settings = this.CoreWebView2.Settings;
                settings.IsGeneralAutofillEnabled = false;
                settings.IsPasswordAutosaveEnabled = false;
                settings.AreDefaultScriptDialogsEnabled = true;
                settings.AreDevToolsEnabled = true;
                settings.AreHostObjectsAllowed = true;
                settings.IsWebMessageEnabled = true;
                settings.AreDefaultContextMenusEnabled = true;
                settings.IsStatusBarEnabled = false;
                settings.IsSwipeNavigationEnabled = false;
                settings.IsPinchZoomEnabled = true;

                // Enable hardware acceleration and smooth scrolling
                settings.IsGeneralAutofillEnabled = false;
                settings.IsPasswordAutosaveEnabled = false;

                // Map local HTML files with DenyCors to avoid CORS issues
                Console.WriteLine($"[ZayitViewer#{_instanceId}] Mapping virtual host 'zayitHost' to path: {HtmlPath}");
                this.CoreWebView2.SetVirtualHostNameToFolderMapping("zayitHost", HtmlPath,
                    CoreWebView2HostResourceAccessKind.DenyCors);

                // Add navigation error handling
                CoreWebView2.NavigationCompleted += (navSender, navArgs) =>
                {
                    Console.WriteLine($"[ZayitViewer#{_instanceId}] Navigation completed. Success: {navArgs.IsSuccess}");
                    if (!navArgs.IsSuccess)
                    {
                        Console.WriteLine($"[ZayitViewer#{_instanceId}] Navigation failed with WebErrorStatus: {navArgs.WebErrorStatus}");
                    }
                };

                CoreWebView2.DOMContentLoaded += (domSender, domArgs) =>
                {
                    Console.WriteLine($"[ZayitViewer#{_instanceId}] DOM content loaded");
                };

                // Unregister existing handlers to prevent duplicates
                Console.WriteLine($"[ZayitViewer#{_instanceId}] Unregistering existing event handlers");
                WebMessageReceived -= ZayitViewer_WebMessageReceived;
                CoreWebView2.DownloadStarting -= CoreWebView2_DownloadStarting;
                CoreWebView2.NavigationCompleted -= OnNavigationCompleted; // Unregister if exists

                // Wire message handler
                Console.WriteLine($"[ZayitViewer#{_instanceId}] Registering WebMessageReceived handler");
                WebMessageReceived += ZayitViewer_WebMessageReceived;

                // Handle download events for Hebrew books
                Console.WriteLine($"[ZayitViewer#{_instanceId}] Registering DownloadStarting handler");
                CoreWebView2.DownloadStarting += CoreWebView2_DownloadStarting;

                // Initialize services now that CoreWebView2 is ready
                Console.WriteLine($"[ZayitViewer#{_instanceId}] Initializing services");
                _services?.InitializePdfManager();

                // Navigate
                Console.WriteLine($"[ZayitViewer#{_instanceId}] Navigating to zayitHost/index.html");
                Source = new Uri("https://zayitHost/index.html");

                // Optional: wait until page is fully loaded before sending messages
                CoreWebView2.NavigationCompleted += OnNavigationCompleted;

                Console.WriteLine($"[ZayitViewer#{_instanceId}] WebView2 setup completed");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ZayitViewer#{_instanceId}] Error in CoreWebView2InitializationCompleted: " + ex);
            }
        }

        private void OnNavigationCompleted(object sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            Console.WriteLine($"[ZayitViewer#{_instanceId}] WebView navigation completed, JS safe to call now");

            // Unregister to prevent multiple calls
            CoreWebView2.NavigationCompleted -= OnNavigationCompleted;
        }

        private void ZayitViewer_WebMessageReceived(object sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            try
            {
                if (e == null || string.IsNullOrWhiteSpace(e.WebMessageAsJson) || _bridge == null)
                    return;

                _bridge.HandleMessage(e.WebMessageAsJson);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ZayitViewer#{_instanceId}] WebView message error: {ex.Message}");
            }
        }

        private void CoreWebView2_DownloadStarting(object sender, CoreWebView2DownloadStartingEventArgs e)
        {
            Console.WriteLine($"[ZayitViewer#{_instanceId}] CoreWebView2_DownloadStarting event fired");
            // Download handling is now managed by services
        }

        // Services are now handled by WebViewBridgeService
        // All communication goes through the modern bridge architecture
    }
}
