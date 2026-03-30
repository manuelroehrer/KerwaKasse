using Microsoft.Win32;
using System.Windows;

namespace KerwaKasse.Helper
{
    public class DialogService : IDialogService
    {
        public void ShowMessage(string message)
        {
            MessageBox.Show(message);
        }

        public void ShowError(string message)
        {
            MessageBox.Show(message, "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
        }

        public string ShowSaveFileDialog(string filter, string title, string defaultFileName)
        {
            var dialog = new SaveFileDialog
            {
                Filter = filter,
                Title = title,
                FileName = defaultFileName
            };

            return dialog.ShowDialog() == true ? dialog.FileName : null;
        }
    }
}
