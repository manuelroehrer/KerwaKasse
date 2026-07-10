using System;
using System.Diagnostics;
using System.Windows;
using KerwaKasse.Core.Services;
using KerwaKasse.Helper;
using ModernWpf.Controls;

namespace KerwaKasse.MVVM.View
{
    public partial class UpdateDialog : ContentDialog
    {
        /// <summary>The user's choice. Closing via X or Escape leaves the default: decide later.</summary>
        public UpdateDialogResult Result { get; private set; } = UpdateDialogResult.Later;

        private readonly string _releaseUrl;

        public UpdateDialog(UpdateInfo update, string installedVersion)
        {
            InitializeComponent();

            VersionText.Text = $"Version {update.Version} ist verfügbar (installiert: {installedVersion}).";

            _releaseUrl = update.ReleaseUrl;
            if (_releaseUrl is null)
                ReleaseLinkText.Visibility = Visibility.Collapsed;

            // Without an installer asset in the release, all the app can offer is the download page.
            if (update.InstallerUrl is null)
                InstallButtonText.Text = "Downloadseite öffnen";

            if (string.IsNullOrWhiteSpace(update.ReleaseNotes))
                NotesViewer.Visibility = Visibility.Collapsed;
            else
                NotesViewer.Document = MarkdownRenderer.ToFlowDocument(update.ReleaseNotes);

            Loaded += (_, _) => AdjustContentHeight();
            SizeChanged += (_, _) => AdjustContentHeight();
        }

        // Cap the content height to the host window so it shrinks on small monitors.
        private void AdjustContentHeight()
        {
            double available = Application.Current?.MainWindow?.ActualHeight ?? SystemParameters.WorkArea.Height;
            RootGrid.MaxHeight = Math.Max(320, available - 72);
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e) => Hide();

        private void ReleaseLink_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo(_releaseUrl) { UseShellExecute = true });
            }
            catch
            {
                // A link that cannot be opened is not worth an error dialog here.
            }
        }

        private void SkipButton_Click(object sender, RoutedEventArgs e)
        {
            Result = UpdateDialogResult.SkipVersion;
            Hide();
        }

        private void InstallButton_Click(object sender, RoutedEventArgs e)
        {
            Result = UpdateDialogResult.InstallNow;
            Hide();
        }
    }
}
