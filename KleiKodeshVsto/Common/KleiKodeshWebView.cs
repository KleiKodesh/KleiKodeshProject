using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace KleiKodesh.Common
{
    /// <summary>
    /// A custom WebView2 control for embedding and interacting with local HTML content.
    /// Supports shared environment for efficiency, virtual host mapping, and JS-to-C# command dispatching.
    /// </summary>
    public class KleiKodeshWebView : WebView2
    {
        private static CoreWebView2Environment _sharedEnvironment;
        private static readonly object _envLock = new object();

        private readonly string _htmlFilePath;
        private object _commandHandler; // Default to self
        private bool _coreInitialized;

        public KleiKodeshWebView(object commandHandler, string htmlFilePath)
        {
            _htmlFilePath = htmlFilePath;
            _commandHandler = commandHandler ?? this;

            Dock = DockStyle.Fill;

            CoreWebView2InitializationCompleted += OnCoreWebView2InitializationCompleted;
            _ = EnsureCoreAsync();
        }

        /// <summary>
        /// Optionally override the command handler.
        /// </summary>
        public void SetCommandHandler(object commandHandler)
        {
            _commandHandler = commandHandler ?? this;
        }

        private async Task EnsureCoreAsync()
        {
            try
            {
                var env = await GetSharedEnvironmentAsync();
                await EnsureCoreWebView2Async(env);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Failed to initialize WebView2: {ex.Message}");
            }
        }

        private static async Task<CoreWebView2Environment> GetSharedEnvironmentAsync()
        {
            if (_sharedEnvironment != null) return _sharedEnvironment;

            lock (_envLock)
            {
                if (_sharedEnvironment != null) return _sharedEnvironment;
            }

            string userDataFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "WebView2SharedCache");

            var env = await CoreWebView2Environment.CreateAsync(userDataFolder: userDataFolder);

            lock (_envLock)
            {
                _sharedEnvironment = env;
            }

            return env;
        }

        private void OnCoreWebView2InitializationCompleted(object sender, CoreWebView2InitializationCompletedEventArgs e)
        {
            if (_coreInitialized || !e.IsSuccess)
            {
                if (!e.IsSuccess)
                    Console.Error.WriteLine($"WebView2 initialization failed: {e.InitializationException?.Message}");
                return;
            }

            _coreInitialized = true;

            try
            {
                string folderPath = Path.GetDirectoryName(_htmlFilePath);
                string filename = Path.GetFileName(_htmlFilePath);
                CoreWebView2.SetVirtualHostNameToFolderMapping("appHost", folderPath, CoreWebView2HostResourceAccessKind.Allow);
                //CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;

                WebMessageReceived += OnWebMessageReceived;
                Source = new Uri($"https://appHost/{filename}");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Post-initialization setup failed: {ex.Message}");
            }
        }

        private void OnWebMessageReceived(object sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            try
            {
                HandleWebMessage(e.WebMessageAsJson);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error handling web message: {ex.Message}");
            }
        }

        private void HandleWebMessage(string json)
        {
            try
            {
                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                    NumberHandling = JsonNumberHandling.AllowReadingFromString,
                    Converters = { new JsonStringEnumConverter() }
                };

                var cmd = JsonSerializer.Deserialize<JsCommand>(json, options);

                if (cmd?.Command == null) return;

                DispatchCommand(cmd);
            }
            catch (JsonException jex)
            {
                Console.Error.WriteLine($"Invalid JSON in web message: {jex.Message}");
                System.Diagnostics.Debug.WriteLine($"Problematic JSON: {json}");
                
                // Robust fallback: manually parse and create command
                try
                {
                    System.Diagnostics.Debug.WriteLine("Attempting fallback JSON parsing...");
                    var doc = JsonDocument.Parse(json);
                    try
                    {
                        var root = doc.RootElement;
                        System.Diagnostics.Debug.WriteLine($"Fallback: Root element type is {root.ValueKind}");
                        
                        // Handle double-encoded JSON (string within string)
                        if (root.ValueKind == JsonValueKind.String)
                        {
                            System.Diagnostics.Debug.WriteLine("Fallback: Detected double-encoded JSON, parsing inner string");
                            var innerJson = root.GetString();
                            var innerDoc = JsonDocument.Parse(innerJson);
                            try
                            {
                                root = innerDoc.RootElement;
                                System.Diagnostics.Debug.WriteLine($"Fallback: Inner root element type is {root.ValueKind}");
                            }
                            finally
                            {
                                // We'll dispose innerDoc at the end
                                doc.Dispose();
                                doc = innerDoc; // Replace the outer doc with inner doc
                            }
                        }
                        
                        if (root.ValueKind != JsonValueKind.Object)
                        {
                            System.Diagnostics.Debug.WriteLine("Fallback: Root is not an object, cannot parse");
                            return;
                        }
                        
                        if (root.TryGetProperty("Command", out var commandProp) && commandProp.ValueKind == JsonValueKind.String)
                        {
                            var command = commandProp.GetString();
                            var args = new JsonElement[0]; // C# 7.3 compatible
                            
                            System.Diagnostics.Debug.WriteLine($"Fallback: Found command '{command}'");
                            
                            // Try to extract Args if present and valid
                            if (root.TryGetProperty("Args", out var argsProp) && argsProp.ValueKind == JsonValueKind.Array)
                            {
                                var argsList = new List<JsonElement>();
                                var argCount = 0;
                                foreach (var arg in argsProp.EnumerateArray())
                                {
                                    argCount++;
                                    // Skip empty objects - they cause issues
                                    if (arg.ValueKind == JsonValueKind.Object && arg.EnumerateObject().Count() == 0)
                                    {
                                        System.Diagnostics.Debug.WriteLine($"Fallback: Skipping empty object at index {argCount - 1}");
                                        continue;
                                    }
                                    argsList.Add(arg);
                                }
                                args = argsList.ToArray();
                                System.Diagnostics.Debug.WriteLine($"Fallback: Processed {argCount} args, kept {args.Length}");
                            }
                            
                            var cmd = new JsCommand { Command = command, Args = args };
                            System.Diagnostics.Debug.WriteLine($"Fallback parsing succeeded for command: {command} with {args.Length} args");
                            DispatchCommand(cmd);
                            return;
                        }
                        else
                        {
                            System.Diagnostics.Debug.WriteLine("Fallback: No valid Command property found");
                        }
                    }
                    finally
                    {
                        doc.Dispose();
                    }
                }
                catch (Exception fallbackEx)
                {
                    Console.Error.WriteLine($"Fallback JSON parsing also failed: {fallbackEx.Message}");
                    System.Diagnostics.Debug.WriteLine($"Fallback error: {fallbackEx}");
                }
            }
        }

        private void DispatchCommand(JsCommand cmd)
        {
            if (_commandHandler == null)
                throw new InvalidOperationException("Command handler not set");

            var method = _commandHandler.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic)
                .FirstOrDefault(m => string.Equals(m.Name, cmd.Command, StringComparison.OrdinalIgnoreCase));

            if (method == null)
            {
                Console.Error.WriteLine($"Command '{cmd.Command}' not found in handler.");
                return;
            }

            try
            {
                var parameters = method.GetParameters();
                var args = new object[parameters.Length];

                for (int i = 0; i < parameters.Length; i++)
                {
                    args[i] = cmd.GetArg(i, parameters[i].ParameterType);
                }

                method.Invoke(_commandHandler, args);
            }
            catch (TargetInvocationException tex)
            {
                Console.Error.WriteLine($"Error invoking command '{cmd.Command}': {tex.InnerException?.Message ?? tex.Message}");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Dispatch error for '{cmd.Command}': {ex.Message}");
            }
        }

        private class JsCommand
        {
            public string Command { get; set; }
            public JsonElement[] Args { get; set; } = new JsonElement[0]; // C# 7.3 compatible

            public object GetArg(int index, Type targetType)
            {
                if (index >= Args.Length) return GetDefault(targetType);
                var element = Args[index];

                try
                {
                    // Handle empty objects as null/default values
                    if (element.ValueKind == JsonValueKind.Object && element.EnumerateObject().Count() == 0)
                    {
                        return GetDefault(targetType);
                    }

                    if (targetType == typeof(int) && element.TryGetInt32(out var intVal)) return intVal;
                    if (targetType == typeof(double) && element.TryGetDouble(out var doubleVal)) return doubleVal;
                    if (targetType == typeof(string) && element.ValueKind == JsonValueKind.String) return element.GetString();
                    if (targetType == typeof(bool) && (element.ValueKind == JsonValueKind.True || element.ValueKind == JsonValueKind.False)) return element.GetBoolean();

                    // For JsonElement type, return the element directly
                    if (targetType == typeof(JsonElement)) return element;

                    // Complex types - use more robust deserialization options
                    var options = new JsonSerializerOptions 
                    { 
                        PropertyNameCaseInsensitive = true,
                        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                        NumberHandling = JsonNumberHandling.AllowReadingFromString
                    };
                    
                    return element.Deserialize(targetType, options);
                }
                catch (JsonException jex)
                {
                    Console.Error.WriteLine($"JSON deserialization error for type {targetType.Name}: {jex.Message}");
                    return GetDefault(targetType);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Argument conversion error for type {targetType.Name}: {ex.Message}");
                    return GetDefault(targetType);
                }
            }

            private static object GetDefault(Type type) => type.IsValueType ? Activator.CreateInstance(type) : null;
        }
    }

}