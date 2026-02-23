using Microsoft.Web.WebView2.WinForms;
using System;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Zayit.Viewer
{
    /// <summary>
    /// Helper class for showing dialogs from WebView2 without freezing
    /// Always uses BeginInvoke to ensure dialogs run on the UI thread
    /// Adjusted for C# 7
    /// </summary>
    public static class WebViewDialogHelper
    {
        public static Task<T> ShowDialogAsync<T>(WebView2 webView, Func<IWin32Window, T> dialogAction)
        {
            var tcs = new TaskCompletionSource<T>();
            IWin32Window parentWindow = GetParentWindow(webView);

            webView.BeginInvoke(new Action(() =>
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
            }));

            return tcs.Task;
        }

        public static Task<T> ShowDialogAsync<T>(WebView2 webView, Func<T> dialogAction)
        {
            return ShowDialogAsync(webView, (parent) => dialogAction());
        }

        private static IWin32Window GetParentWindow(WebView2 webView)
        {
            try
            {
                var parentForm = webView.FindForm();
                if (parentForm != null) return parentForm;

                Control parent = webView.Parent;
                while (parent != null)
                {
                    if (parent is IWin32Window win32Window) return win32Window;
                    parent = parent.Parent;
                }
            }
            catch { }

            return null;
        }

        public static Task<string> ShowOpenFileDialogAsync(WebView2 webView, string filter = null, string title = null)
        {
            return ShowDialogAsync(webView, (parent) =>
            {
                using (var dialog = new OpenFileDialog())
                {
                    if (!string.IsNullOrEmpty(filter)) dialog.Filter = filter;
                    if (!string.IsNullOrEmpty(title)) dialog.Title = title;
                    dialog.CheckFileExists = true;
                    dialog.CheckPathExists = true;

                    return dialog.ShowDialog(parent) == DialogResult.OK ? dialog.FileName : null;
                }
            });
        }

        public static Task<string> ShowSaveFileDialogAsync(WebView2 webView, string filter = null, string title = null, string defaultFileName = null, string initialDirectory = null)
        {
            return ShowDialogAsync(webView, (parent) =>
            {
                using (var dialog = new SaveFileDialog())
                {
                    if (!string.IsNullOrEmpty(filter)) dialog.Filter = filter;
                    if (!string.IsNullOrEmpty(title)) dialog.Title = title;
                    if (!string.IsNullOrEmpty(defaultFileName)) dialog.FileName = defaultFileName;
                    if (!string.IsNullOrEmpty(initialDirectory)) dialog.InitialDirectory = initialDirectory;

                    return dialog.ShowDialog(parent) == DialogResult.OK ? dialog.FileName : null;
                }
            });
        }

        public static Task<string> ShowFolderBrowserDialogAsync(WebView2 webView, string description = null)
        {
            return ShowDialogAsync(webView, (parent) =>
            {
                using (var dialog = new FolderBrowserDialog())
                {
                    if (!string.IsNullOrEmpty(description)) dialog.Description = description;

                    return dialog.ShowDialog(parent) == DialogResult.OK ? dialog.SelectedPath : null;
                }
            });
        }
    }
}
