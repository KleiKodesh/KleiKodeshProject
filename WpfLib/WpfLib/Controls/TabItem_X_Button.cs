using Microsoft.Web.WebView2.Wpf;
using System.Windows.Controls;
using WebViewLib;
using WpfLib.Helpers;

namespace WpfLib.Controls
{
    public class TabItem_X_Button : Button
    {

        public TabItem_X_Button() 
        {
            this.Click += TabControl_X_Button_Click;
        }

        private  void TabControl_X_Button_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            if (sender is Button button)
            {
                TabItem tabItem = DependencyHelper.FindParent<TabItem>(button);

                if (tabItem != null && tabItem.Parent is TabControl tabControl)
                {
                    tabControl.Items.Remove(tabItem);
                    try { DependencyHelper.FindChild<WebViewHost>(tabItem)?.WebView.Dispose(); } catch { }
                    try { DependencyHelper.FindChild<WebView2>(tabItem)?.Dispose(); } catch { }
                }
            }
        }
    }
}
