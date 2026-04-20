using RegexFindLib.Helpers;
using System.Windows.Controls;

namespace RegexFindLib.UI
{
    /// <summary>
    /// Entry point for the RegexFind task pane.
    /// Instantiate from the VSTO ribbon, passing the Word application instance.
    /// </summary>
    public partial class RegexFindView : UserControl
    {
        public RegexFindView(
            Microsoft.Office.Interop.Word.Application app,
            Microsoft.Office.Tools.Word.ApplicationFactory factory)
        {
            Vsto.Application = app;
            Vsto.ApplicationFactory = factory;
            InitializeComponent();

            // Pre-load fonts and styles once the view is loaded
            Loaded += (_, __) =>
            {
                if (DataContext is RegexFindViewModel vm)
                {
                    vm.LoadFontsCommand.Execute(null);
                    vm.LoadStylesCommand.Execute(null);
                }
            };
        }
    }
}
