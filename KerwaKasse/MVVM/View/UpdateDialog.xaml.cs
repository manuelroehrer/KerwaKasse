using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
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

            if (update.ReleaseNotes.All(r => string.IsNullOrWhiteSpace(r.Text)))
                NotesViewer.Visibility = Visibility.Collapsed;
            else
                NotesViewer.Document = BuildNotesDocument(update.ReleaseNotes);

            Loaded += (_, _) => AdjustContentHeight();
            SizeChanged += (_, _) => AdjustContentHeight();
        }

        // A single version shows just its notes. Several (the user skipped some) are listed newest
        // first, each under a heading with version and date, so everything that changed since the
        // installed version is in one scrollable list.
        private static FlowDocument BuildNotesDocument(IReadOnlyList<ReleaseNote> releases)
        {
            if (releases.Count == 1)
                return MarkdownRenderer.ToFlowDocument(releases[0].Text);

            var document = MarkdownRenderer.CreateDocument();
            foreach (var release in releases)
            {
                document.Blocks.Add(CreateVersionHeading(release, isFirst: document.Blocks.Count == 0));
                MarkdownRenderer.AppendMarkdown(document.Blocks, release.Text);
            }
            return document;
        }

        // Larger than the headings inside the notes ("Neuerungen", "Fixes") and in the accent color
        // with a rule below, so the version boundaries stand out while scrolling.
        private static Paragraph CreateVersionHeading(ReleaseNote release, bool isFirst)
        {
            var heading = new Paragraph
            {
                FontSize = 18,
                FontWeight = FontWeights.SemiBold,
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xED, 0xEF, 0xF2)),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(0, 0, 0, 6),
                Margin = new Thickness(0, isFirst ? 0 : 24, 0, 8)
            };
            heading.SetResourceReference(TextElement.ForegroundProperty, "AppAccentBrush");
            heading.Inlines.Add(new Run($"Version {release.Version}"));
            if (release.PublishedAt is DateTimeOffset published)
            {
                heading.Inlines.Add(new Run($"  ·  {published.ToLocalTime():dd.MM.yyyy}")
                {
                    FontSize = 14,
                    FontWeight = FontWeights.Normal,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x58, 0x60, 0x70))
                });
            }
            return heading;
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
