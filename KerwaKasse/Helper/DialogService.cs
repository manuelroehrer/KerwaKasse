using KerwaKasse.Core.Services;
using KerwaKasse.MVVM.View;
using KerwaKasse.MVVM.ViewModel;
using Microsoft.Win32;
using ModernWpf.Controls;
using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

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

        public string ShowOpenFileDialog(string filter, string title)
        {
            var dialog = new OpenFileDialog
            {
                Filter = filter,
                Title = title,
                CheckFileExists = true
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

        public async Task<bool> ShowWarningConfirmationAsync(string title, string message, string warning, string question = null)
        {
            var panel = new StackPanel { MaxWidth = 460 };
            if (!string.IsNullOrEmpty(message))
                panel.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) });
            panel.Children.Add(BuildWarningBox(warning));
            if (!string.IsNullOrEmpty(question))
                panel.Children.Add(new TextBlock { Text = question, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 0) });

            var dialog = new ContentDialog
            {
                Title = title,
                Content = panel,
                PrimaryButtonText = "Ja",
                CloseButtonText = "Abbrechen",
                DefaultButton = ContentDialogButton.Close
            };

            return await dialog.ShowAsync() == ContentDialogResult.Primary;
        }

        // Recreates the amber warning callout from the info page (glyph + wrapped text) for use in a dialog.
        private static Border BuildWarningBox(string text)
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var glyph = new TextBlock
            {
                Text = "",
                FontFamily = new FontFamily("Segoe MDL2 Assets"),
                FontSize = 16,
                Foreground = new SolidColorBrush(Color.FromRgb(0xB8, 0x86, 0x0B)),
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 1, 10, 0)
            };
            Grid.SetColumn(glyph, 0);

            var body = new TextBlock
            {
                Text = text,
                TextWrapping = TextWrapping.Wrap,
                FontSize = 14,
                LineHeight = 20,
                Foreground = new SolidColorBrush(Color.FromRgb(0x6B, 0x5A, 0x22))
            };
            Grid.SetColumn(body, 1);

            grid.Children.Add(glyph);
            grid.Children.Add(body);

            return new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0xFB, 0xF5, 0xE6)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xEB, 0xD9, 0xA8)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(12, 10, 12, 10),
                Child = grid
            };
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

        public async Task<UpdateDialogResult> ShowUpdateAvailableAsync(UpdateInfo update, string installedVersion)
        {
            var dialog = new UpdateDialog(update, installedVersion);
            await dialog.ShowAsync();
            return dialog.Result;
        }

        public async Task<string> ShowDownloadProgressAsync(string title, Func<IProgress<double>, CancellationToken, Task<string>> download)
        {
            var bar = new System.Windows.Controls.ProgressBar
            {
                Minimum = 0,
                Maximum = 100,
                Margin = new Thickness(0, 14, 0, 0)
            };
            var panel = new StackPanel { MinWidth = 360 };
            panel.Children.Add(new TextBlock { Text = "Das Update wird heruntergeladen ...", TextWrapping = TextWrapping.Wrap });
            panel.Children.Add(bar);

            var dialog = new ContentDialog
            {
                Title = title,
                Content = panel,
                CloseButtonText = "Abbrechen"
            };

            using var cancellation = new CancellationTokenSource();
            // Progress is created on the UI thread, so its callbacks are marshalled back here.
            var progress = new Progress<double>(fraction => bar.Value = Math.Min(100, fraction * 100));
            var downloadTask = download(progress, cancellation.Token);

            // The dialog has no OK button; it closes itself the moment the download ends, however it ends.
            dialog.Opened += (_, _) => downloadTask.ContinueWith(
                _ => dialog.Hide(),
                CancellationToken.None,
                TaskContinuationOptions.None,
                TaskScheduler.FromCurrentSynchronizationContext());

            await dialog.ShowAsync();

            // Dialog closed while the download still runs — the user pressed "Abbrechen".
            if (!downloadTask.IsCompleted)
                cancellation.Cancel();

            try
            {
                return await downloadTask;
            }
            catch (OperationCanceledException)
            {
                return null;
            }
        }
    }
}
