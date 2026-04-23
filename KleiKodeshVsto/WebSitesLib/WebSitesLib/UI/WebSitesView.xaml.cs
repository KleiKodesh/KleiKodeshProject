using System;
using System.Windows.Controls;

namespace WebSitesLib.UI
{
    public partial class WebSitesView : UserControl
    {
        public WebSitesView()
        {
            InitializeComponent();
        }

        private void PopOutButton_Click(object sender, System.Windows.RoutedEventArgs e) =>
             TogglePopOut?.Invoke();

        /// <summary>
        /// Set by the host to handle the popout toggle.
        /// </summary>
        public Action TogglePopOut { get; set; }

        /// <summary>
        /// Called by TaskPaneManager via reflection to wire up the popout toggle.
        /// </summary>
        public void SetPopOutToggleAction(Action action) => TogglePopOut = action;
    }
}
