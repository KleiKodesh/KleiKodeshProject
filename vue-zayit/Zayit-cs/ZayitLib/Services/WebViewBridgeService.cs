using Microsoft.Web.WebView2.WinForms;
using System;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace Zayit.Services
{
    public class WebViewBridgeService
    {
        private readonly WebView2 _webView;
        private readonly object _serviceProvider;

        public WebViewBridgeService(WebView2 webView, object serviceProvider)
        {
            _webView = webView;
            _serviceProvider = serviceProvider;
        }

        public async void HandleMessage(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return;

            try
            {
                // Check if the JSON is double-encoded (string containing JSON)
                string actualJson = json;
                if (json.StartsWith("\"") && json.EndsWith("\""))
                {
                    // It's a JSON string, so deserialize it first to get the actual JSON
                    actualJson = JsonSerializer.Deserialize<string>(json);
                }

                // Try to deserialize using JsonDocument first to debug the structure
                using (var document = JsonDocument.Parse(actualJson))
                {
                    var root = document.RootElement;
                    
                    var id = root.GetProperty("id").GetString();
                    var method = root.GetProperty("method").GetString();
                    
                    JsonElement[] parameters = Array.Empty<JsonElement>();
                    if (root.TryGetProperty("params", out var paramsElement) && paramsElement.ValueKind == JsonValueKind.Array)
                    {
                        parameters = paramsElement.EnumerateArray().ToArray();
                    }
                    
                    var msg = new Message { id = id, method = method, @params = parameters };
                
                    if (msg == null || string.IsNullOrWhiteSpace(msg.method) || string.IsNullOrWhiteSpace(msg.id))
                    {
                        await SendResponse(msg?.id ?? Guid.NewGuid().ToString(), null, "Method name is required");
                        return;
                    }
                    
                    if (msg.method == "GetLinks")
                    {
                        Console.WriteLine($"[WebViewBridge] GetLinks called with {msg.@params?.Length ?? 0} parameters");
                    }
                    var result = await Execute(msg.method, msg.@params ?? Array.Empty<JsonElement>());
                    await SendResponse(msg.id, result, null);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[WebViewBridge] Error: {ex.Message}");
                try
                {
                    var msg = JsonSerializer.Deserialize<Message>(json);
                    await SendResponse(msg?.id ?? Guid.NewGuid().ToString(), null, ex.Message);
                }
                catch 
                {
                    await SendResponse(Guid.NewGuid().ToString(), null, "Failed to parse message");
                }
            }
        }

        private bool HasResponseFields(string json)
        {
            return json.Contains("\"result\"") || json.Contains("\"error\"");
        }

        private async Task<object> Execute(string method, JsonElement[] parameters)
        {
            if (string.IsNullOrWhiteSpace(method))
                throw new ArgumentException("Method name cannot be null or empty", nameof(method));
            
            if (_serviceProvider == null)
                throw new InvalidOperationException("ServiceProvider is not initialized");
            
            var methodInfo = _serviceProvider.GetType().GetMethod(method, BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic);
            if (methodInfo == null)
                throw new InvalidOperationException($"Method '{method}' not found");

            var paramInfos = methodInfo.GetParameters();
            var args = new object[paramInfos.Length];

            // Convert JSON parameters to method arguments
            for (int i = 0; i < paramInfos.Length && i < (parameters?.Length ?? 0); i++)
            {
                var paramType = paramInfos[i].ParameterType;
                
                try
                {
                    if (paramType == typeof(string))
                        args[i] = parameters[i].GetString();
                    else if (paramType == typeof(int))
                        args[i] = parameters[i].GetInt32();
                    else if (paramType == typeof(bool))
                        args[i] = parameters[i].GetBoolean();
                    else if (paramType == typeof(object[]))
                    {
                        var arrayElements = parameters[i].EnumerateArray().ToArray();
                        var objArray = new object[arrayElements.Length];
                        for (int j = 0; j < arrayElements.Length; j++)
                        {
                            // Convert JsonElement to actual primitive values
                            var element = arrayElements[j];
                            switch (element.ValueKind)
                            {
                                case JsonValueKind.String:
                                    objArray[j] = element.GetString();
                                    break;
                                case JsonValueKind.Number:
                                    if (element.TryGetInt32(out int intVal))
                                        objArray[j] = intVal;
                                    else if (element.TryGetInt64(out long longVal))
                                        objArray[j] = longVal;
                                    else
                                        objArray[j] = element.GetDouble();
                                    break;
                                case JsonValueKind.True:
                                case JsonValueKind.False:
                                    objArray[j] = element.GetBoolean();
                                    break;
                                case JsonValueKind.Null:
                                    objArray[j] = null;
                                    break;
                                default:
                                    // For complex objects, use raw text deserialization
                                    objArray[j] = JsonSerializer.Deserialize<object>(element.GetRawText());
                                    break;
                            }
                        }
                        args[i] = objArray;
                    }
                    else
                        args[i] = JsonSerializer.Deserialize(parameters[i].GetRawText(), paramType);
                }
                catch (Exception ex)
                {
                    throw new ArgumentException($"Failed to convert parameter {i} to {paramType.Name}: {ex.Message}");
                }
            }
            
            try
            {
                var result = methodInfo.Invoke(_serviceProvider, args);
                
                // Handle async methods
                if (result is Task task)
                {
                    await task;
                    if (task.GetType().IsGenericType)
                    {
                        var property = task.GetType().GetProperty("Result");
                        result = property?.GetValue(task);
                    }
                    else
                    {
                        result = null;
                    }
                }

                return result;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[WebViewBridge] Error invoking {method}: {ex.Message}");
                throw;
            }
        }

        private async Task SendResponse(string id, object result, string error)
        {
            try
            {
                var response = new { id, result, error };
                var responseJson = JsonSerializer.Serialize(response);
                // Call the JavaScript callback directly instead of using postMessage
                var script = $"if (window.handleBridgeResponse) {{ window.handleBridgeResponse({responseJson}); }}";
                
                if (_webView.InvokeRequired)
                {
                    var tcs = new TaskCompletionSource<bool>();
                    _webView.BeginInvoke(new Action(() =>
                    {
                        try
                        {
                            var task = _webView.ExecuteScriptAsync(script);
                            task.ContinueWith(t =>
                            {
                                if (t.IsFaulted)
                                    tcs.SetException(t.Exception);
                                else
                                    tcs.SetResult(true);
                            });
                        }
                        catch (Exception ex)
                        {
                            tcs.SetException(ex);
                        }
                    }));
                    await tcs.Task;
                }
                else
                {
                    await _webView.ExecuteScriptAsync(script);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[WebViewBridge] Error sending response: {ex.Message}");
            }
        }

        private class Message
        {
            public string id { get; set; }
            public string method { get; set; }
            public JsonElement[] @params { get; set; } = Array.Empty<JsonElement>();
        }
    }
}