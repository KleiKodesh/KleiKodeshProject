using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Zayit.Viewer
{
    public class ZayitViewer : WebView2
    {
        private static CoreWebView2Environment _sharedEnvironment;
        private static readonly object _envLock = new object();
        private static int _instanceCounter = 0;
        private readonly int _instanceId;

        private object _commandHandler;
        private ZayitViewerCommands _commands;
        private readonly string HtmlPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Html");

        private bool _coreInitialized;

        public ZayitViewer(object commandHandler = null)
        {
            _instanceId = ++_instanceCounter;
            Console.WriteLine($"[ZayitViewer] Creating instance #{_instanceId}");
            
            this.Dock = DockStyle.Fill;

            // Initialize commands first
            _commands = new ZayitViewerCommands(this);
            SetCommandHandler(commandHandler);

            // Wire initialization event
            this.CoreWebView2InitializationCompleted += ZayitViewer_CoreWebView2InitializationCompleted;

            // Fire-and-forget async safely
            _ = EnsureCoreAsyncSafe();
        }

        public void SetCommandHandler(object commandHandler)
        {
            _commandHandler = commandHandler ?? _commands ?? throw new InvalidOperationException("Command handler not initialized");
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

            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Html",
                                       "ZayitWebView2SharedCache");

            var env = await CoreWebView2Environment.CreateAsync(userDataFolder: path);

            lock (_envLock)
            {
                _sharedEnvironment = env; // save for all instances
            }

            return _sharedEnvironment;
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
                
                // Map local HTML files with DenyCors to avoid CORS issues
                this.CoreWebView2.SetVirtualHostNameToFolderMapping("zayitHost", HtmlPath,
                    CoreWebView2HostResourceAccessKind.DenyCors);

                // Unregister existing handlers to prevent duplicates
                Console.WriteLine($"[ZayitViewer#{_instanceId}] Unregistering existing event handlers");
                WebMessageReceived -= ZayitViewer_WebMessageReceived;
                CoreWebView2.DownloadStarting -= CoreWebView2_DownloadStarting;

                // Wire message handler
                Console.WriteLine($"[ZayitViewer#{_instanceId}] Registering WebMessageReceived handler");
                WebMessageReceived += ZayitViewer_WebMessageReceived;

                // Handle download events for Hebrew books
                Console.WriteLine($"[ZayitViewer#{_instanceId}] Registering DownloadStarting handler");
                CoreWebView2.DownloadStarting += CoreWebView2_DownloadStarting;

                // Initialize Hebrew books download manager
                Console.WriteLine($"[ZayitViewer#{_instanceId}] Initializing Hebrew books download manager");
                var commands = _commandHandler as ZayitViewerCommands;
                commands?.InitializeHebrewBooksDownloadManager(CoreWebView2);

                // Initialize PDF manager
                Console.WriteLine($"[ZayitViewer#{_instanceId}] Initializing PDF manager");
                _ = commands?.InitializePdfManager(); // Fire-and-forget async

                // Navigate
                Console.WriteLine($"[ZayitViewer#{_instanceId}] Navigating to zayitHost/index.html");
                Source = new Uri("https://zayitHost/index.html");

                // Optional: wait until page is fully loaded before sending messages
                CoreWebView2.NavigationCompleted += (_, __) =>
                {
                    Console.WriteLine($"[ZayitViewer#{_instanceId}] WebView navigation completed, JS safe to call now");
                };
                
                Console.WriteLine($"[ZayitViewer#{_instanceId}] WebView2 setup completed");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ZayitViewer#{_instanceId}] Error in CoreWebView2InitializationCompleted: " + ex);
            }
        }

        private void ZayitViewer_WebMessageReceived(object sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            try
            {
                Console.WriteLine($"[ZayitViewer#{_instanceId}] WebMessageReceived event fired");
                HandleWebMessage(e.WebMessageAsJson);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ZayitViewer#{_instanceId}] WebView message handler error: {ex}");
            }
        }

        private void CoreWebView2_DownloadStarting(object sender, CoreWebView2DownloadStartingEventArgs e)
        {
            Console.WriteLine($"[ZayitViewer#{_instanceId}] CoreWebView2_DownloadStarting event fired");
            // NOTE: All download handling logic should be implemented in ZayitViewerCommands
            var commands = _commandHandler as ZayitViewerCommands;
            commands?.HandleDownloadStarting(e);
        }

        private void HandleWebMessage(string json)
        {
            try
            {
                Console.WriteLine($"[WebMessage] Received: {json}");

                var cmd = JsonSerializer.Deserialize<JsCommand>(
                    json,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (cmd?.Command is null)
                {
                    Console.WriteLine("[WebMessage] Command is null, ignoring");
                    return;
                }

                Console.WriteLine($"[WebMessage] Parsed command: {cmd.Command} with {cmd.Args?.Length ?? 0} args");

                DispatchCommand(cmd);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[WebMessage] Error processing message: {ex}");
            }
        }

        private void DispatchCommand(JsCommand cmd)
        {
            try
            {
                Console.WriteLine($"[Command] Dispatching command: {cmd.Command}");
                
                var target = _commandHandler ?? throw new InvalidOperationException("Command handler is null");

                var method = target.GetType()
                    .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic)
                    .FirstOrDefault(m => string.Equals(m.Name, cmd.Command, StringComparison.OrdinalIgnoreCase));

                if (method == null)
                {
                    Console.WriteLine($"[Command] No handler found for: {cmd.Command}");
                    return;
                }

                Console.WriteLine($"[Command] Found handler method: {method.Name}");

                var parameters = method.GetParameters();
                var args = new object[parameters.Length];

                Console.WriteLine($"[Command] Method expects {parameters.Length} parameters");

                for (int i = 0; i < parameters.Length; i++)
                {
                    args[i] = cmd.GetArg(i, parameters[i].ParameterType);
                    Console.WriteLine($"[Command] Parameter {i}: {args[i]} (type: {parameters[i].ParameterType.Name})");
                }

                Console.WriteLine($"[Command] Invoking method: {method.Name}");
                
                method.Invoke(target, args);
                
                Console.WriteLine($"[Command] Method invoked successfully: {method.Name}");
            }
            catch (TargetInvocationException tie)
            {
                Console.WriteLine($"[Command] Handler threw exception: {tie.InnerException}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Command] Dispatch failed: {ex}");
            }
        }

        private class JsCommand
        {
            public string Command { get; set; }
            public JsonElement[] Args { get; set; } = Array.Empty<JsonElement>();

            public object GetArg(int index, Type targetType)
            {
                if (index >= Args.Length)
                    return null;

                if (targetType == typeof(int) && Args[index].ValueKind == JsonValueKind.Number)
                {
                    if (Args[index].TryGetDouble(out double doubleValue))
                        return (int)Math.Round(doubleValue);
                }

                return Args[index].Deserialize(targetType, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
        }

        // Example method for testing
        private void ShowAlert(string message)
        {
            Debug.WriteLine("From JS: " + message);
        }
    }
}
