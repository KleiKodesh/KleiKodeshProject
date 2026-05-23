using System.Windows.Controls;

namespace Nakdan
{
    // ═══════════════════════════════════════════════════════════
    //  NAKDAN VIEW — code-behind
    //
    //  All converters live in Converters.cs (same namespace).
    //  The ViewModel is injected via Initialize() rather than
    //  set in XAML so a live Word.Application can be passed in.
    //
    //  Usage in ThisAddIn.cs:
    //      var view = new NakdanView();
    //      view.Initialize(new NakdanApi(this.Application));
    //      // host view in a CustomTaskPane
    // ═══════════════════════════════════════════════════════════
    public partial class NakdanView : UserControl
    {
        private NakdanViewModel _viewModel;

        public NakdanView()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Wires the ViewModel to this View.
        /// Call once from the add-in host before the control is shown.
        /// </summary>
        public void Initialize(NakdanApi api)
        {
            _viewModel  = new NakdanViewModel(api);
            DataContext = _viewModel;
        }

        /// <summary>
        /// Injects a pre-built ViewModel (useful for unit tests).
        /// </summary>
        public void SetViewModel(NakdanViewModel vm)
        {
            _viewModel  = vm;
            DataContext = vm;
        }
    }
}
