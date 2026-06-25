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

        public string ShowSaveFileDialog(string filter, string title, string defaultFileName, string initialDirectory = null)
        {
            var dialog = new SaveFileDialog
            {
                Filter = filter,
                Title = title,
                FileName = defaultFileName
                // OverwritePrompt stays at its default (true): if the user deliberately picks an
                // existing file, Windows shows the standard "replace?" warning — their choice wins.
            };
            if (!string.IsNullOrEmpty(initialDirectory))
                dialog.InitialDirectory = initialDirectory;

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

        public async Task<string> ShowTextInputAsync(string title, string label, string initialValue)
        {
            var inputBox = new TextBox
            {
                Text = initialValue ?? string.Empty,
                MaxLength = 40,
                FontSize = 15,
                Margin = new Thickness(0, 6, 0, 0)
            };

            var panel = new StackPanel { MinWidth = 320 };
            if (!string.IsNullOrEmpty(label))
                panel.Children.Add(new TextBlock { Text = label, FontSize = 13, Foreground = System.Windows.Media.Brushes.Gray });
            panel.Children.Add(inputBox);

            var dialog = new ContentDialog
            {
                Title = title,
                Content = panel,
                PrimaryButtonText = "Speichern",
                CloseButtonText = "Abbrechen",
                DefaultButton = ContentDialogButton.Primary
            };

            inputBox.Loaded += (_, _) => { inputBox.SelectAll(); inputBox.Focus(); };

            if (await dialog.ShowAsync() != ContentDialogResult.Primary)
                return null;

            var value = inputBox.Text?.Trim() ?? string.Empty;
            return value.Length == 0 ? null : value;
        }

        public async Task ShowSavedAnalysisManagementAsync(SavedAnalysisManagementViewModel viewModel)
        {
            var dialog = new SavedAnalysisManagementDialog { DataContext = viewModel };
            await dialog.ShowAsync();
        }
    }
}
