using System;
using System.Windows;
using System.Windows.Input;

namespace WpfLib.Controls
{
    /// <summary>
    /// Interaction logic for InputBox.xaml
    /// </summary>
    public partial class HebrewInputBox : Window
    {
        public HebrewInputBox(string title = "", string prompt = "", string defaultText = "")
        {
            InitializeComponent();
            Title = title;
            lblQuestion.Content = prompt;
            txtAnswer.Text = defaultText;
        }

        private void btnDialogOk_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = true;
        }

        private void Window_ContentRendered(object sender, EventArgs e)
        {
            txtAnswer.SelectAll();
            txtAnswer.Focus();
        }

        public string Answer
        {
            get { return txtAnswer.Text; }
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            switch (e.Key) 
            {
                case Key.Enter:
                    this.DialogResult = true;
                    Close();
                    e.Handled = true;
                    break;
                case Key.Escape:
                    Close();
                    e.Handled= true;
                    break;
            } 
                
        }
    }
}
