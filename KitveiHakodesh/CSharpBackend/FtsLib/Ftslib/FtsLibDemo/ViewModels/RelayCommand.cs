using System;
using System.Windows.Input;

namespace FtsLibDemo.ViewModels
{
    /// <summary>
    /// A simple ICommand implementation that delegates Execute and CanExecute to lambdas.
    /// </summary>
    public sealed class RelayCommand : ICommand
    {
        private readonly Action<object> _execute;
        private readonly Func<object, bool> _canExecute;

        public RelayCommand(Action<object> execute, Func<object, bool> canExecute = null)
        {
            _execute    = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public RelayCommand(Action execute, Func<bool> canExecute = null)
            : this(_ => execute(), canExecute == null ? (Func<object, bool>)null : _ => canExecute())
        {
        }

        public event EventHandler CanExecuteChanged
        {
            add    => CommandManager.RequerySuggested += value;
            remove => CommandManager.RequerySuggested -= value;
        }

        public bool CanExecute(object parameter) => _canExecute == null || _canExecute(parameter);
        public void Execute(object parameter)    => _execute(parameter);

        /// <summary>Forces WPF to re-evaluate CanExecute for all commands.</summary>
        public static void RaiseCanExecuteChanged() =>
            CommandManager.InvalidateRequerySuggested();
    }
}
