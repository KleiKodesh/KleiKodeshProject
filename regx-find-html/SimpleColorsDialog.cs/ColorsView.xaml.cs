using RegexInWord.Helpers;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;

namespace RegexInWord.SimpleColorsDialog
{
    /// <summary>
    /// Interaction logic for View.xaml
    /// </summary>
    public partial class ColorsView : Window
    {
        public KeyValuePair<string, int?> Result { get; private set; }

        public ColorsView(string deafultHex = null, int? defaultDecimal = null)
        {
            InitializeComponent();
            Result = new KeyValuePair<string, int?>(deafultHex, defaultDecimal);
            this.Loaded += ColorsView_Loaded;
        }

        private void ColorsView_Loaded(object sender, RoutedEventArgs e)
        {
            switch (ThemingHelper.WpfStartupLocation)
            {
                case 1:
                    this.WindowStartupLocation = WindowStartupLocation.Manual;
                    this.Left = 100;
                    break;

                case 2:
                    this.WindowStartupLocation = WindowStartupLocation.Manual;
                    this.Left = SystemParameters.WorkArea.Right - this.Width - 50;
                    break;

                default:
                    this.WindowStartupLocation = WindowStartupLocation.CenterOwner;
                    break;
            }
            this.Top = 200;
            this.Loaded -= ColorsView_Loaded;
        }

        void CloseDialoge(string hexValue, int? decimalValue)
        {
            Result = new KeyValuePair<string, int?>(hexValue, decimalValue);
            DialogResult = true;
            Close();
        }

        private void Window_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Escape)
                Close();
        }

        private void AutoColorButton_Click(object sender, RoutedEventArgs e)
        {
            CloseDialoge("#000000", "#000000".HexToInt());
        }

        private void NoColorButton_Click(object sender, RoutedEventArgs e) =>
           CloseDialoge(null, null);

        private void ListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is ListBox listBox)
            {
                if (listBox.SelectedItem is ColorsHelper.ColorModel colorModel)
                    CloseDialoge(colorModel.HexValue, colorModel.DecimalValue);
                else
                    CloseDialoge(listBox.SelectedItem.ToString(), listBox.SelectedItem.ToString().HexToInt());
            }
        }

        private void MoreColors_Click(object sender, RoutedEventArgs e)
        {
            if (StaticColorPicker.ColorPicker.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                string hexColor = StaticColorPicker.HexColor;
                CloseDialoge(hexColor, hexColor.HexToInt());
            }
        }

        private void X_Button_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}
