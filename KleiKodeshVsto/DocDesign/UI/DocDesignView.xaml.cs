using System;
using System.Windows.Controls;
using System.Windows.Input;

namespace DocDesign
{
    /// <summary>
    /// Interaction logic for DocDesignView2.xaml
    /// </summary>
    public partial class DocDesignView : UserControl
    {
        public DocDesignView(Microsoft.Office.Interop.Word.Application app, Microsoft.Office.Tools.Word.ApplicationFactory factory)
        {
            Vsto.Application = app;
            Vsto.ApplicationFactory = factory;
            InitializeComponent();
        }
    }
}
