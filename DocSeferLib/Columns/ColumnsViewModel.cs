using Microsoft.Office.Interop.Word;
using WpfLib;
using WpfLib.ViewModels;

namespace DocSeferLib.Columns
{
    public class ColumnsViewModel : ViewModelBase
    {
        public AlignColumns AlignColumns { get; } = new AlignColumns();
        public RelayCommand AlignColumnsCommand => new RelayCommand(() => AlignColumns.Apply());
        public RelayCommand OpenColumnsDialogCommand => new RelayCommand(() => Vsto.Application.Dialogs[WdWordDialog.wdDialogFormatColumns].Show());
        public RelayCommand FindNextUnevenColumnsCommand => new RelayCommand(() => AlignColumns.FindNext());

        public ColumnsViewModel()
        {

        }
    }
}
