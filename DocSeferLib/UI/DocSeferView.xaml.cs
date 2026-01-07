using System;
using System.Windows.Controls;
using System.Windows.Input;

namespace DocSeferLib
{
    /// <summary>
    /// Interaction logic for DocSeferLibView2.xaml
    /// </summary>
    public partial class DocSeferLibView : UserControl
    {
        public DocSeferLibView(Microsoft.Office.Interop.Word.Application app, Microsoft.Office.Tools.Word.ApplicationFactory factory)
        {
            Vsto.Application = app;
            Vsto.ApplicationFactory = factory;
            InitializeComponent();
        }
    }
}
