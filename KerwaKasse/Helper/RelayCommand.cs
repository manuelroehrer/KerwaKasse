using System;
using System.Windows.Input;

namespace KerwaKasse.Helper
{
    /// <summary>
    /// Hilfsklasse zum Ausführen von Commands
    /// </summary>
    public class RelayCommand : ICommand
    {
        //Delegate zum Ausführen der gewünschten Methode
        private Action<object> execute;
        //Delegate zum testen ob gewünschte Methode ausgeführt werden kann
        private Func<object, bool> canExecute;

        public RelayCommand(Action<object> ex, Func<object, bool> canex = null)
        {
            execute = ex;
            canExecute = canex;
        }

        /// <summary>
        /// Ruft übergebenen Methode auf (falls vorhanden), ob Command-Mehtode ausgeführt werden darf
        /// </summary>
        /// <param name="parameter"></param>
        /// <returns></returns>
        public bool CanExecute(object parameter)
        {
            return canExecute is null ? true : canExecute(parameter);
        }

        /// <summary>
        /// Führt Command-Methode aus
        /// </summary>
        /// <param name="parameter"></param>
        public void Execute(object parameter)
        {
            execute(parameter);
        }

        /// <summary>
        /// EventHandler für Commands
        /// </summary>
        public event EventHandler CanExecuteChanged
        {
            add { CommandManager.RequerySuggested += value; }
            remove { CommandManager.RequerySuggested -= value; }
        }
    }
}
