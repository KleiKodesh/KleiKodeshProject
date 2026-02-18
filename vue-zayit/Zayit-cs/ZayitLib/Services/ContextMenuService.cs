using Microsoft.Web.WebView2.WinForms;
using System;
using System.Windows.Forms;

namespace Zayit.Services
{
    /// <summary>
    /// Service for managing context menu functionality in ZayitViewer
    /// </summary>
    public class ContextMenuService : IDisposable
    {
        private readonly WebView2 _webView;
        private Action _toggleVisibilityAction;

        public ContextMenuService(WebView2 webView)
        {
            _webView = webView ?? throw new ArgumentNullException(nameof(webView));
        }

        /// <summary>
        /// Set the action to toggle host visibility
        /// </summary>
        public void SetToggleVisibilityAction(Action toggleAction)
        {
            _toggleVisibilityAction = toggleAction;
        }

        /// <summary>
        /// Initialize context menu with custom items
        /// </summary>
        public void InitializeContextMenu()
        {
            if (_webView.CoreWebView2 == null)
            {
                Console.WriteLine("[ContextMenuService] CoreWebView2 not initialized yet");
                return;
            }

            _webView.CoreWebView2.ContextMenuRequested += CoreWebView2_ContextMenuRequested;
            Console.WriteLine("[ContextMenuService] Context menu initialized");
        }

        private void CoreWebView2_ContextMenuRequested(object sender, Microsoft.Web.WebView2.Core.CoreWebView2ContextMenuRequestedEventArgs e)
        {
            try
            {
                var menuItems = e.MenuItems;

                // Separator
                var separator = _webView.CoreWebView2.Environment.CreateContextMenuItem(
                    "-",
                    null,
                    Microsoft.Web.WebView2.Core.CoreWebView2ContextMenuItemKind.Separator
                );
                menuItems.Add(separator);

                // Toggle visibility item
                var toggleItem = _webView.CoreWebView2.Environment.CreateContextMenuItem(
                    "החלף תצוגה: חלון / חלונית צד",
                    null,
                    Microsoft.Web.WebView2.Core.CoreWebView2ContextMenuItemKind.Command
                );
                toggleItem.CustomItemSelected += (s, args) =>
                {
                    Console.WriteLine("[ContextMenuService] Toggle visibility menu item clicked");
                    _toggleVisibilityAction?.Invoke();
                };
                menuItems.Add(toggleItem);

                var separator2 = _webView.CoreWebView2.Environment.CreateContextMenuItem(
                    "-",
                    null,
                    Microsoft.Web.WebView2.Core.CoreWebView2ContextMenuItemKind.Separator
                );
                menuItems.Add(separator2);

                // PrintScreen item
                var printScreenItem = _webView.CoreWebView2.Environment.CreateContextMenuItem(
                    "כלי החיתוך",
                    null,
                    Microsoft.Web.WebView2.Core.CoreWebView2ContextMenuItemKind.Command
                );
                printScreenItem.CustomItemSelected += (s, args) =>
                {
                    Console.WriteLine("[ContextMenuService] Print Screen menu item clicked");
                    SendPrintScreen();
                };
                menuItems.Add(printScreenItem);

                Console.WriteLine("[ContextMenuService] Context menu items added");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ContextMenuService] Error adding context menu items: {ex}");
            }
        }

        private void SendPrintScreen()
        {
            try
            {
                // Simulate PrintScreen key
                SendKeys.SendWait("{PRTSC}");
                Console.WriteLine("[ContextMenuService] PrintScreen triggered");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ContextMenuService] Error triggering PrintScreen: {ex}");
            }
        }

        /// <summary>
        /// Cleanup context menu event handlers
        /// </summary>
        public void Dispose()
        {
            if (_webView?.CoreWebView2 != null)
            {
                _webView.CoreWebView2.ContextMenuRequested -= CoreWebView2_ContextMenuRequested;
            }
        }
    }
}
