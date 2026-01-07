using System.Windows;
using System.Windows.Controls;
using WpfLib.Helpers;
using WpfLib.ViewModels;

namespace WpfLib.Controls
{
    public static class WindowButtons
    {
        public static RelayCommand<Button> CloseCommand => 
            new RelayCommand<Button>((button) => Close(button));

        public static RelayCommand<Button> MinimizeCommand =>
            new RelayCommand<Button>((button) => Minimize(button));

        public static RelayCommand<Button> MaximizeRestoreCommand =>
            new RelayCommand<Button>((button) => MaximizeRestore(button));

        static void Close(Button button)
        {
            var window = DependencyHelper.FindParent<Window>(button);
            if (window != null)
               window.Close();
        }

        static void Minimize(Button button)
        {
            var window = DependencyHelper.FindParent<Window>(button);
            if (window != null)
                window.WindowState = WindowState.Minimized;
        }
               

        static void MaximizeRestore(Button button)
        {
            var window = DependencyHelper.FindParent<Window>(button);
            if (window != null)
                window.WindowState = window.WindowState == WindowState.Maximized ? 
                    WindowState.Normal : WindowState.Maximized;
        }


    }
}
