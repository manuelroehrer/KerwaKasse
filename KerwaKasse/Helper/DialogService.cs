using KerwaKasse.MVVM.View;
using KerwaKasse.MVVM.ViewModel;
using Microsoft.Win32;
using ModernWpf.Controls;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

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

        public async Task<bool> ShowConfirmationAsync(string title, string message)
        {
            var dialog = new ContentDialog
            {
                Title = title,
                Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, MaxWidth = 440 },
                PrimaryButtonText = "Ja",
                CloseButtonText = "Abbrechen",
                DefaultButton = ContentDialogButton.Close
            };

            return await dialog.ShowAsync() == ContentDialogResult.Primary;
        }

        public async Task ShowMenuManagementAsync(MenuManagementViewModel viewModel)
        {
            var dialog = new MenuManagementDialog { DataContext = viewModel };
            await dialog.ShowAsync();
        }
    }
}
