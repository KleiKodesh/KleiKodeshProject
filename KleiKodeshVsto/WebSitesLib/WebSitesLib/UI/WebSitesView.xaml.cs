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
             TogglePopOut?.Invoke(false);

        /// <summary>
        /// Set by the host to handle the popout toggle.
        /// The bool parameter indicates whether to enter fullscreen mode after popping out.
        /// </summary>
        public Action<bool> TogglePopOut { get; set; }

        /// <summary>
        /// Called by TaskPaneManager via reflection to wire up the popout toggle.
        /// </summary>
        public void SetPopOutToggleAction(Action<bool> action) => TogglePopOut = action;
    }
}
