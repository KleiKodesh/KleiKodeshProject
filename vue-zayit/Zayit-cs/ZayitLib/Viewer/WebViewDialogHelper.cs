using Microsoft.Web.WebView2.WinForms;
using System;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Zayit.Viewer
{
    /// <summary>
    /// Helper class for showing dialogs from WebView2 without freezing
    /// Handles proper threading and parent window management
    /// </summary>
    public static class WebViewDialogHelper
    {
        /// <summary>
        /// Show a dialog asynchronously without blocking the WebView2 message pump
        /// </summary>
        /// <typeparam name="T">Type of dialog result</typeparam>
        /// <param name="webView">The WebView2 control</param>
        /// <param name="dialogAction">Function that shows the dialog and returns the result</param>
        /// <returns>The dialog result</returns>
        public static async Task<T> ShowDialogAsync<T>(WebView2 webView, Func<IWin32Window, T> dialogAction)
        {
            var tcs = new TaskCompletionSource<T>();

            // Get the proper parent window
            IWin32Window parentWindow = GetParentWindow(webView);

            // Show dialog on UI thread with proper async handling
            if (webView.InvokeRequired)
            {
                webView.BeginInvoke(new Action(() => {
                    try
                    {
                        T result = dialogAction(parentWindow);
                        tcs.SetResult(result);
                    }
                    catch (Exception ex)
                    {
                        tcs.SetException(ex);
                    }
                }));
            }
            else
            {
                try
                {
                    T result = dialogAction(parentWindow);
                    tcs.SetResult(result);
                }
                catch (Exception ex)
                {
                    tcs.SetException(ex);
                }
            }

            // Wait for dialog result without blocking WebView2 message pump
            return await tcs.Task;
        }

        /// <summary>
        /// Show a simple dialog asynchronously (no parent window needed)
        /// </summary>
        /// <typeparam name="T">Type of dialog result</typeparam>
        /// <param name="webView">The WebView2 control</param>
        /// <param name="dialogAction">Function that shows the dialog and returns the result</param>
        /// <returns>The dialog result</returns>
        public static async Task<T> ShowDialogAsync<T>(WebView2 webView, Func<T> dialogAction)
        {
            return await ShowDialogAsync(webView, (parent) => dialogAction());
        }

        /// <summary>
        /// Get the proper parent window for dialogs
        /// </summary>
        /// <param name="webView">The WebView2 control</param>
        /// <returns>Parent window or null if not found</returns>
        private static IWin32Window GetParentWindow(WebView2 webView)
        {
            try
            {
                // Try to get the WebView2 parent form
                var parentForm = webView.FindForm();
                if (parentForm != null)
                {
                    return parentForm;
                }

                // Try to get any parent control that implements IWin32Window
                Control parent = webView.Parent;
                while (parent != null)
                {
                    if (parent is IWin32Window win32Window)
                    {
                        return win32Window;
                    }
                    parent = parent.Parent;
                }
            }
            catch
            {
                // If we can't get the parent, return null
            }

            return null;
        }

        /// <summary>
        /// Show an OpenFileDialog asynchronously
        /// </summary>
        /// <param name="webView">The WebView2 control</param>
        /// <param name="filter">File filter (e.g., "PDF Files (*.pdf)|*.pdf")</param>
        /// <param name="title">Dialog title</param>
        /// <returns>Selected file path or null if cancelled</returns>
        public static async Task<string> ShowOpenFileDialogAsync(WebView2 webView, string filter = null, string title = null)
        {
            return await ShowDialogAsync(webView, (parent) =>
            {
                using (var dialog = new OpenFileDialog())
                {
                    if (!string.IsNullOrEmpty(filter))
                        dialog.Filter = filter;
                    if (!string.IsNullOrEmpty(title))
                        dialog.Title = title;
                    
                    dialog.CheckFileExists = true;
                    dialog.CheckPathExists = true;

                    DialogResult result = parent != null 
                        ? dialog.ShowDialog(parent)
                        : dialog.ShowDialog();

                    return result == DialogResult.OK ? dialog.FileName : null;
                }
            });
        }

        /// <summary>
        /// Show a SaveFileDialog asynchronously
        /// </summary>
        /// <param name="webView">The WebView2 control</param>
        /// <param name="filter">File filter (e.g., "PDF Files (*.pdf)|*.pdf")</param>
        /// <param name="title">Dialog title</param>
        /// <param name="defaultFileName">Default filename (optional)</param>
        /// <param name="initialDirectory">Initial directory (optional)</param>
        /// <returns>Selected file path or null if cancelled</returns>
        public static async Task<string> ShowSaveFileDialogAsync(WebView2 webView, string filter = null, string title = null, string defaultFileName = null, string initialDirectory = null)
        {
            return await ShowDialogAsync(webView, (parent) =>
            {
                using (var dialog = new SaveFileDialog())
                {
                    if (!string.IsNullOrEmpty(filter))
                        dialog.Filter = filter;
                    if (!string.IsNullOrEmpty(title))
                        dialog.Title = title;
                    if (!string.IsNullOrEmpty(defaultFileName))
                        dialog.FileName = defaultFileName;
                    if (!string.IsNullOrEmpty(initialDirectory))
                        dialog.InitialDirectory = initialDirectory;

                    DialogResult result = parent != null 
                        ? dialog.ShowDialog(parent)
                        : dialog.ShowDialog();

                    return result == DialogResult.OK ? dialog.FileName : null;
                }
            });
        }

        /// <summary>
        /// Show a FolderBrowserDialog asynchronously
        /// </summary>
        /// <param name="webView">The WebView2 control</param>
        /// <param name="description">Dialog description</param>
        /// <returns>Selected folder path or null if cancelled</returns>
        public static async Task<string> ShowFolderBrowserDialogAsync(WebView2 webView, string description = null)
        {
            return await ShowDialogAsync(webView, (parent) =>
            {
                using (var dialog = new FolderBrowserDialog())
                {
                    if (!string.IsNullOrEmpty(description))
                        dialog.Description = description;

                    DialogResult result = parent != null 
                        ? dialog.ShowDialog(parent)
                        : dialog.ShowDialog();

                    return result == DialogResult.OK ? dialog.SelectedPath : null;
                }
            });
        }
    }
}