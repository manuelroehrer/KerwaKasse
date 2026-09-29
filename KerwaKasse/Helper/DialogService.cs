using KerwaKasse.Core.Services;
using KerwaKasse.MVVM.View;
using KerwaKasse.MVVM.ViewModel;
using Microsoft.Win32;
using ModernWpf.Controls;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
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
        private static Border BuildWarningBox(string text) => BuildWarningBox(new Run(text));

        private static Border BuildWarningBox(params Inline[] text)
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
                TextWrapping = TextWrapping.Wrap,
                FontSize = 14,
                LineHeight = 20,
                Foreground = new SolidColorBrush(Color.FromRgb(0x6B, 0x5A, 0x22))
            };
            body.Inlines.AddRange(text);
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

        public async Task<NewEventRequest> ShowNewEventDialogAsync(IReadOnlyList<string> existingNames, Func<string, bool> isValidBackup)
        {
            var errorBrush = new SolidColorBrush(Color.FromRgb(0xC4, 0x2B, 0x1C));
            var nameBox = new TextBox
            {
                MaxLength = 40,
                FontSize = 15,
                Margin = new Thickness(0, 6, 0, 0)
            };
            // The layout never changes size while the dialog is open (a growing and shrinking dialog looks
            // jumpy): hints that come and go only switch between Visible and Hidden, which keeps their
            // space, and the import controls are always there, just disabled unless the import is chosen.
            var nameTakenHint = new TextBlock
            {
                Text = "Dieser Name ist bereits vergeben.",
                FontSize = 13,
                Foreground = errorBrush,
                Margin = new Thickness(0, 4, 0, 0),
                Visibility = Visibility.Hidden
            };

            // Radio buttons sharing one parent panel form a group on their own.
            var emptyOption = new RadioButton { Content = "Leere Veranstaltung anlegen", IsChecked = true, FontSize = 15 };
            var backupOption = new RadioButton { Content = "Sicherung (Backup-Datei) importieren", FontSize = 15 };

            // The backup file is picked and checked here, inside the dialog, so "Anlegen" only runs once
            // name and file are both known to be fine.
            var chooseFileButton = new Button
            {
                Content = BuildIconLabel(new TextBlock { Text = "", FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 14 }, "Datei auswählen"),
                FontSize = 15
            };
            // One line for the chosen file: its name, or in red why it cannot be used.
            var fileStatus = new TextBlock
            {
                Text = " ",
                FontSize = 13,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(0, 6, 0, 0),
                Visibility = Visibility.Hidden
            };
            var importPanel = new StackPanel { Margin = new Thickness(28, 6, 0, 0), IsEnabled = false, Opacity = 0.5 };
            importPanel.Children.Add(chooseFileButton);
            importPanel.Children.Add(fileStatus);
            importPanel.Children.Add(new TextBlock
            {
                Text = "Die Sicherung wird als zusätzliche Veranstaltung übernommen. Die aktive Veranstaltung bleibt dabei unverändert.",
                TextWrapping = TextWrapping.Wrap,
                FontSize = 13,
                Foreground = System.Windows.Media.Brushes.Gray,
                Margin = new Thickness(0, 8, 0, 0)
            });

            var panel = new StackPanel { MinWidth = 400, MaxWidth = 440 };
            panel.Children.Add(new TextBlock { Text = "Name", FontSize = 13, Foreground = System.Windows.Media.Brushes.Gray });
            panel.Children.Add(nameBox);
            panel.Children.Add(nameTakenHint);
            panel.Children.Add(new TextBlock
            {
                Text = "Inhalt",
                FontSize = 13,
                Foreground = System.Windows.Media.Brushes.Gray,
                Margin = new Thickness(0, 16, 0, 2)
            });
            panel.Children.Add(emptyOption);
            panel.Children.Add(backupOption);
            panel.Children.Add(importPanel);

            var dialog = new ContentDialog
            {
                Title = BuildDialogTitle("", "Neue Veranstaltung"),
                Content = panel,
                PrimaryButtonText = "Anlegen",
                CloseButtonText = "Abbrechen",
                DefaultButton = ContentDialogButton.Primary,
                IsPrimaryButtonEnabled = false
            };

            string backupPath = null;
            bool backupValid = false;

            void Validate()
            {
                string name = nameBox.Text.Trim();
                bool taken = existingNames.Any(n => string.Equals(n.Trim(), name, StringComparison.CurrentCultureIgnoreCase));
                nameTakenHint.Visibility = taken ? Visibility.Visible : Visibility.Hidden;

                bool importing = backupOption.IsChecked == true;
                importPanel.IsEnabled = importing;
                importPanel.Opacity = importing ? 1 : 0.5;
                dialog.IsPrimaryButtonEnabled = name.Length > 0 && !taken && (!importing || backupValid);
            }

            chooseFileButton.Click += (_, _) =>
            {
                // A native file dialog, so it can open on top of this ContentDialog.
                string path = ShowOpenFileDialog("SQLite Datenbank (*.db)|*.db|Alle Dateien (*.*)|*.*", "Sicherung auswählen ...");
                if (string.IsNullOrEmpty(path)) return;

                backupPath = path;
                backupValid = isValidBackup(path);
                string fileName = System.IO.Path.GetFileName(path);
                fileStatus.Text = backupValid ? fileName : $"„{fileName}“ ist keine gültige KerwaKasse-Datenbank.";
                fileStatus.Foreground = backupValid ? System.Windows.Media.Brushes.Gray : errorBrush;
                fileStatus.ToolTip = path;
                fileStatus.Visibility = Visibility.Visible;
                Validate();
            };
            nameBox.TextChanged += (_, _) => Validate();
            nameBox.Loaded += (_, _) => nameBox.Focus();
            emptyOption.Checked += (_, _) => Validate();
            backupOption.Checked += (_, _) => Validate();

            if (await dialog.ShowAsync() != ContentDialogResult.Primary)
                return null;

            return new NewEventRequest(nameBox.Text.Trim(), backupOption.IsChecked == true ? backupPath : null);
        }

        // Dialog heading with a leading icon, in the style of the Speisekarten / Auswertungen management
        // dialogs (accent-coloured glyph, semibold title).
        private static StackPanel BuildDialogTitle(string glyph, string text)
        {
            var title = new StackPanel { Orientation = Orientation.Horizontal };
            title.Children.Add(new TextBlock
            {
                Text = glyph,
                FontFamily = new FontFamily("Segoe MDL2 Assets"),
                FontSize = 20,
                Foreground = (Brush)Application.Current.FindResource("AppAccentBrush"),
                VerticalAlignment = VerticalAlignment.Center
            });
            title.Children.Add(new TextBlock
            {
                Text = text,
                FontSize = 21,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(0x1B, 0x1E, 0x24)),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(12, 0, 0, 0)
            });
            return title;
        }

        // Button content "icon + text", laid out like the buttons on the info page.
        private static StackPanel BuildIconLabel(FrameworkElement icon, string text)
        {
            icon.VerticalAlignment = VerticalAlignment.Center;
            icon.Margin = new Thickness(0, 0, 10, 0);
            var content = new StackPanel { Orientation = Orientation.Horizontal };
            content.Children.Add(icon);
            content.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center });
            return content;
        }

        public async Task<bool> ShowDeleteEventDialogAsync(string eventName, string contents, Func<string> createBackup)
        {
            var panel = new StackPanel { MinWidth = 400, MaxWidth = 460 };
            panel.Children.Add(new TextBlock { Text = contents, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) });
            panel.Children.Add(BuildWarningBox(
                new Run("Die Veranstaltung wird mit allen Produkten, Speisekarten, Verkäufen und Auswertungen gelöscht. "),
                new Bold(new Run("Es wird empfohlen, vor dem Löschen ein Backup zu erstellen."))));

            // The backup is made from within this dialog, so it also works for an event that is not the
            // active one (the info page's backup button only covers the active event). Same icon as on the
            // info page, but a regular button: the accent style would be too prominent here.
            var backupButton = new Button { FontSize = 15, Margin = new Thickness(0, 12, 0, 0) };
            var backupIcon = new System.Windows.Shapes.Path
            {
                Data = (Geometry)Application.Current.FindResource("BackupIcon"),
                Stretch = Stretch.Uniform
            };
            backupIcon.SetBinding(System.Windows.Shapes.Shape.FillProperty,
                new System.Windows.Data.Binding(nameof(Button.Foreground)) { Source = backupButton });
            backupButton.Content = BuildIconLabel(new Viewbox { Width = 16, Height = 16, Child = backupIcon }, "Backup erstellen");
            // One reserved line (Hidden, not Collapsed), so the dialog keeps its size when it appears.
            var backupStatus = new TextBlock
            {
                Text = " ",
                TextTrimming = TextTrimming.CharacterEllipsis,
                FontSize = 13,
                Foreground = new SolidColorBrush(Color.FromRgb(0x10, 0x7C, 0x10)),
                Margin = new Thickness(0, 6, 0, 0),
                Visibility = Visibility.Hidden
            };
            backupButton.Click += (_, _) =>
            {
                string path = createBackup();
                if (path is null) return;
                backupStatus.Text = "Backup gespeichert: " + System.IO.Path.GetFileName(path);
                backupStatus.ToolTip = path;
                backupStatus.Visibility = Visibility.Visible;
            };
            panel.Children.Add(backupButton);
            panel.Children.Add(backupStatus);

            var prompt = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 20, 0, 0) };
            prompt.Inlines.Add(new Run("Zur Bestätigung bitte den Namen der Veranstaltung eingeben: "));
            prompt.Inlines.Add(new Bold(new Run(eventName)));
            panel.Children.Add(prompt);
            var confirmBox = new TextBox { FontSize = 15, Margin = new Thickness(0, 6, 0, 0) };
            panel.Children.Add(confirmBox);

            var dialog = new ContentDialog
            {
                Title = "Veranstaltung löschen",
                Content = panel,
                PrimaryButtonText = "Veranstaltung löschen",
                CloseButtonText = "Abbrechen",
                DefaultButton = ContentDialogButton.Close,
                IsPrimaryButtonEnabled = false
            };

            confirmBox.TextChanged += (_, _) => dialog.IsPrimaryButtonEnabled =
                string.Equals(confirmBox.Text.Trim(), eventName.Trim(), StringComparison.CurrentCultureIgnoreCase);

            return await dialog.ShowAsync() == ContentDialogResult.Primary;
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
