using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using System;
using System.Diagnostics;
using System.Drawing;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Forms;
using WebViewLib;

namespace KleiKodesh.RibbonSettings
{
    public class WebViewControl : UserControl
    {
        CoreWebView2Environment _environment;
        public WebView2 WebView { get; }
        private ProgressBar progressBar;

        public WebViewControl()
        {
            WebView = new WebView2 { Dock = DockStyle.Fill };
            WebView.WebMessageReceived += WebView_WebMessageReceived;
            WebView.NavigationCompleted += WebView_NavigationCompleted;
            ThemeManager.Theme.PropertyChanged += (_, e) => SetColor(e.PropertyName);
            Controls.Add(WebView);
            LoadProgressBar();
        }

        async void SetColor(string propertyName)
        {
            if (propertyName == "Foreground")
            {
                string color = ThemeManager.ColorToRgbString(ThemeManager.Theme.Foreground);
                await ExecuteScriptAsync($@"document.body.style.color = ""{color}"";");
            }
            else if (propertyName == "Background")
            {
                string color = ThemeManager.ColorToRgbString(ThemeManager.Theme.Background);
                await ExecuteScriptAsync($@"document.body.style.background = ""{color}"";");
            }
        }

        void LoadProgressBar()
        {
            progressBar = new ProgressBar
            {
                Height = 25,
                Width = 200, // set a fixed width
                Dock = DockStyle.None,
                Anchor = AnchorStyles.None
            };

            // Center it within the parent control (e.g., "this" if inside a Form or Panel)
            progressBar.Location = new Point(
                (this.ClientSize.Width - progressBar.Width) / 2,
                (this.ClientSize.Height - progressBar.Height) / 2
            );

            // Optional: update position on resize
            this.Resize += (s, e) =>
            {
                progressBar.Location = new Point(
                    (this.ClientSize.Width - progressBar.Width) / 2,
                    (this.ClientSize.Height - progressBar.Height) / 2
                );
            };

            Controls.Add(progressBar);
        }

        public async Task EnsurCoreAsync()
        {
            if (_environment == null)
            {
                string tempWebCacheDir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                _environment = await CoreWebView2Environment.CreateAsync(userDataFolder: tempWebCacheDir);
            }

            await WebView.EnsureCoreWebView2Async(_environment);
        }

        public async Task Navigate(string url)
        {
            progressBar.Style = ProgressBarStyle.Marquee;
            progressBar.Visible = true;
            //await EnsurCoreAsync();
            //WebView.CoreWebView2.DOMContentLoaded += (_, __) =>
            //{
            //    SetColor("Foreground");
            //    SetColor("Background");
            //};

            WebView.Source = new Uri("file:///" + url.Replace("\\", "/"));
            //WebView.CoreWebView2.Navigate(url); 
        }

        private void WebView_NavigationCompleted(object sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            progressBar.Style = ProgressBarStyle.Continuous;
            progressBar.Visible = false;        
        }


        //{ "action": "set",  "target": "SomeProperty", "value": "SomeValue"}
        //{ "action": "call", "target": "SomeMethod"}
        private void WebView_WebMessageReceived(object sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            var json = JsonSerializer.Deserialize<JsonElement>(e.WebMessageAsJson);
            string action = json.GetProperty("action").GetString();
            string target = json.GetProperty("target").GetString();

            var type = GetType();
            var targetObject = this;

            switch (action)
            {
                case "set":
                    var prop = type.GetProperty(target, BindingFlags.Public | BindingFlags.Instance);
                    if (prop?.CanWrite == true)
                    {
                        var raw = json.GetProperty("value").GetRawText();
                        var value = JsonSerializer.Deserialize(raw, prop.PropertyType);
                        prop.SetValue(targetObject, value);
                    }
                    else Console.WriteLine($"Property '{target}' not found or not writable.");
                    break;

                case "call":
                    var method = type.GetMethod(target, BindingFlags.Public | BindingFlags.Instance);
                    if (method?.GetParameters().Length == 0)
                        method.Invoke(targetObject, null);
                    else Debug.WriteLine($"Method '{target}' not found or requires parameters.");
                    break;

                default:
                    Debug.WriteLine("Unsupported action.");
                    break;
            }
        }

        public async Task SendAsJson(object data)
        {
            if (WebView.IsDisposed) return;
            await EnsurCoreAsync();
            string json = JsonSerializer.Serialize(data);
            WebView.CoreWebView2.PostWebMessageAsJson(json);
        }

        public async Task<string> ExecuteScriptAsync(string script)
        {
            if (WebView.IsDisposed) return string.Empty;
            await EnsurCoreAsync();
            return await WebView.ExecuteScriptAsync(script);
        }
    }
}
