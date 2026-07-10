using KerwaKasse.Core.Services;
using KerwaKasse.Helper;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;

namespace KerwaKasse.MVVM.ViewModel
{
    public class InfoViewModel : PropertyChangedBase
    {
        // Single hard-coded source of the project location; the update service derives its API
        // endpoint from it. Should the repository ever move, both change together here.
        public const string RepositoryUrl = "https://github.com/manuelroehrer/KerwaKasse";

        private const string CheckOnStartupKey = "updateCheckOnStartup";
        private const string SkippedVersionKey = "updateSkippedVersion";

        // Lets the window render and the first view load before the network check starts.
        private static readonly TimeSpan StartupCheckDelay = TimeSpan.FromSeconds(3);

        public RelayCommand BackupCommand { get; }
        public RelayCommand RestoreCommand { get; }
        public RelayCommand OpenDataFolderCommand { get; }
        public RelayCommand OpenRepositoryCommand { get; }
        public RelayCommand OpenReleasesCommand { get; }
        public RelayCommand OpenThirdPartyNoticesCommand { get; }
        public RelayCommand CheckForUpdatesCommand { get; }

        // App name, version and copyright are read from the assembly so they
        // stay in sync with the .csproj and never need to be maintained twice.
        public string Version { get; }
        public string Copyright { get; }

        private string _updateStatusText = string.Empty;
        /// <summary>Feedback line next to the manual check button ("KerwaKasse 2.9.0 ist aktuell", ...).</summary>
        public string UpdateStatusText
        {
            get => _updateStatusText;
            private set { _updateStatusText = value; OnPropertyChanged(); }
        }

        private bool _updateStatusIsPositive;
        /// <summary>True while the status line reports "up to date" — shows the green check mark.</summary>
        public bool UpdateStatusIsPositive
        {
            get => _updateStatusIsPositive;
            private set { _updateStatusIsPositive = value; OnPropertyChanged(); }
        }

        /// <summary>True when the startup check was switched off by hand in settings.json. The info
        /// page shows a small notice then, so a forgotten switch does not go unnoticed.</summary>
        public bool StartupCheckDisabled => !_settingsService.Get(CheckOnStartupKey, true);

        private bool _isCheckingForUpdates;

        private readonly IDialogService _dialogService;
        private readonly IDatabaseBackupService _backupService;
        private readonly IUpdateService _updateService;
        private readonly ISettingsService _settingsService;
        private readonly ILogger<InfoViewModel> _logger;
        private readonly string _dbFilePath;
        private readonly string _dataFolder;
        private readonly Version _currentVersion;
        private readonly string _currentVersionText;

        public InfoViewModel(IDialogService dialogService, string dbFilePath, IDatabaseBackupService backupService, IUpdateService updateService, ISettingsService settingsService, ILogger<InfoViewModel> logger)
        {
            _dialogService = dialogService;
            _backupService = backupService;
            _updateService = updateService;
            _settingsService = settingsService;
            _logger = logger;
            _dbFilePath = dbFilePath;
            _dataFolder = Path.GetDirectoryName(dbFilePath) ?? string.Empty;

            var assembly = Assembly.GetExecutingAssembly();
            _currentVersion = assembly.GetName().Version ?? new Version(0, 0, 0);
            _currentVersionText = $"{_currentVersion.Major}.{_currentVersion.Minor}.{_currentVersion.Build}";
            Version = $"Version {_currentVersionText}";
            Copyright = assembly.GetCustomAttribute<AssemblyCopyrightAttribute>()?.Copyright
                        ?? "© Manuel Röhrer";

            // Write the default once so the key is discoverable in settings.json — the setting has
            // no UI on purpose (see CheckForUpdatesOnStartupAsync).
            if (_settingsService.SetIfAbsent(CheckOnStartupKey, true))
                _settingsService.Save();

            BackupCommand = new RelayCommand(o => BackupDatabase());
            RestoreCommand = new RelayCommand(async o => await RestoreDatabaseAsync());
            OpenDataFolderCommand = new RelayCommand(o => OpenDataFolder());
            OpenRepositoryCommand = new RelayCommand(o => OpenUrl(RepositoryUrl));
            OpenReleasesCommand = new RelayCommand(o => OpenUrl(RepositoryUrl + "/releases"));
            OpenThirdPartyNoticesCommand = new RelayCommand(o => OpenThirdPartyNotices());
            CheckForUpdatesCommand = new RelayCommand(async o => await CheckForUpdatesManuallyAsync());
        }

        /// <summary>The quiet background check after app start. Only ever surfaces when a new,
        /// not-skipped version is found; being offline or up to date stays invisible.</summary>
        public async Task CheckForUpdatesOnStartupAsync()
        {
            // Deliberately without any UI: normal users should not be able to switch the check off
            // with one (possibly accidental) click and then permanently miss important fixes. Setting
            // "Updates.CheckOnStartup" to false in settings.json remains as a quiet escape hatch.
            if (!_settingsService.Get(CheckOnStartupKey, true))
            {
                _logger.LogDebug("Startup update check disabled in settings");
                return;
            }

            try
            {
                await Task.Delay(StartupCheckDelay);
                var result = await _updateService.CheckForUpdateAsync(_currentVersion);
                if (result.Status != UpdateCheckStatus.UpdateAvailable) return;

                if (result.Update!.Version.ToString() == _settingsService.Get<string>(SkippedVersionKey))
                {
                    _logger.LogInformation("Update {Version} not offered: version is marked as skipped", result.Update.Version);
                    return;
                }

                await OfferUpdateAsync(result.Update);
            }
            catch (Exception ex)
            {
                // Whatever goes wrong here (e.g. another dialog already open when the offer appears),
                // the background check must never disturb the start of the app.
                _logger.LogWarning(ex, "Startup update check failed unexpectedly");
            }
        }

        // The explicit check from the info page. Unlike the startup check it gives feedback in every
        // outcome and also offers a version the user skipped earlier — asking again is the point here.
        private async Task CheckForUpdatesManuallyAsync()
        {
            // Quiet reentry guard instead of disabling the button: a second click while a check (or
            // its dialog) is still open would try to show a second ContentDialog, which WPF forbids.
            if (_isCheckingForUpdates) return;
            _isCheckingForUpdates = true;
            UpdateStatusIsPositive = false;
            UpdateStatusText = "Suche nach Update ...";
            try
            {
                var result = await _updateService.CheckForUpdateAsync(_currentVersion);
                switch (result.Status)
                {
                    case UpdateCheckStatus.UpdateAvailable:
                        UpdateStatusText = $"Version {result.Update!.Version} ist verfügbar.";
                        await OfferUpdateAsync(result.Update);
                        break;
                    case UpdateCheckStatus.UpToDate:
                        UpdateStatusText = $"KerwaKasse {_currentVersionText} ist aktuell";
                        UpdateStatusIsPositive = true;
                        break;
                    default:
                        UpdateStatusText = "Prüfung zurzeit nicht möglich (keine Internetverbindung?).";
                        break;
                }
            }
            finally
            {
                _isCheckingForUpdates = false;
            }
        }

        private async Task OfferUpdateAsync(UpdateInfo update)
        {
            switch (await _dialogService.ShowUpdateAvailableAsync(update, _currentVersionText))
            {
                case UpdateDialogResult.InstallNow:
                    await InstallUpdateAsync(update);
                    break;
                case UpdateDialogResult.SkipVersion:
                    _settingsService.Set(SkippedVersionKey, update.Version.ToString());
                    _settingsService.Save();
                    _logger.LogInformation("Version {Version} marked as skipped", update.Version);
                    break;
            }
        }

        private async Task InstallUpdateAsync(UpdateInfo update)
        {
            // A release without an installer asset can only be offered as a manual download.
            if (update.InstallerUrl is null)
            {
                OpenUrl(update.ReleaseUrl ?? RepositoryUrl);
                return;
            }

            try
            {
                _logger.LogInformation("Downloading installer for update {Version}", update.Version);
                string installerPath = await _dialogService.ShowDownloadProgressAsync(
                    "Update wird heruntergeladen",
                    (progress, cancellationToken) => _updateService.DownloadInstallerAsync(update, progress, cancellationToken));
                if (installerPath is null)
                {
                    _logger.LogInformation("Update download cancelled");
                    return;
                }

                // /SILENT shows only the small progress window instead of the full wizard.
                // /FORCECLOSEAPPLICATIONS is a backstop in case this process has not fully exited by
                // the time the installer replaces the files. /AUTORELAUNCH=1 is our own switch the
                // setup script evaluates to start KerwaKasse again once the update is through.
                Process.Start(new ProcessStartInfo(installerPath, "/SILENT /FORCECLOSEAPPLICATIONS /AUTORELAUNCH=1")
                {
                    UseShellExecute = true
                });
                _logger.LogInformation("Installer for update {Version} started, exiting", update.Version);
                Application.Current.Shutdown();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Update to {Version} failed", update.Version);
                _dialogService.ShowError("Das Update konnte nicht heruntergeladen oder gestartet werden: " + ex.Message);
            }
        }

        private void OpenUrl(string url)
        {
            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to open URL {Url}", url);
                _dialogService.ShowError("Der Link konnte nicht geöffnet werden: " + ex.Message);
            }
        }

        // Opens the third-party notices that ship next to the executable (copied into the
        // build output by the project file, so the installer picks them up automatically).
        private void OpenThirdPartyNotices()
        {
            var path = Path.Combine(AppContext.BaseDirectory, "THIRD-PARTY-NOTICES.md");
            if (!File.Exists(path))
            {
                _dialogService.ShowError("Die Datei THIRD-PARTY-NOTICES.md wurde nicht gefunden.");
                return;
            }

            try
            {
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            }
            catch (Exception)
            {
                // No association for .md files — fall back to Notepad, which is always there.
                try
                {
                    Process.Start(new ProcessStartInfo("notepad.exe", $"\"{path}\""));
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to open third-party notices");
                    _dialogService.ShowError("Die Datei konnte nicht geöffnet werden: " + ex.Message);
                }
            }
        }

        private void BackupDatabase()
        {
            string defaultFileName = "kerwakasse_backup_" + DateTime.Now.ToString("dd.MM.yyyy_HH.mm");

            string filePath = _dialogService.ShowSaveFileDialog(
                "SQLite Datenbank (*.db)|*.db",
                "Speicherort für Backup wählen ...",
                defaultFileName);

            if (filePath != null)
            {
                try
                {
                    File.Copy(_dbFilePath, filePath, true);
                    _logger.LogInformation("Database backup created: {FilePath}", filePath);
                    _dialogService.ShowMessage("Backup erfolgreich erstellt.");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Backup to {FilePath} failed", filePath);
                    _dialogService.ShowError("Backup fehlgeschlagen: " + ex.Message);
                }
            }
        }

        // Replaces the live database with a chosen backup file. An empty (never-used) database is replaced
        // straight away; a database that already holds data needs an explicit confirmation first, with the
        // counts spelled out and a reminder that the step is irreversible.
        private async Task RestoreDatabaseAsync()
        {
            string sourcePath = _dialogService.ShowOpenFileDialog(
                "SQLite Datenbank (*.db)|*.db|Alle Dateien (*.*)|*.*",
                "Datenbank zum Wiederherstellen wählen ...");
            if (string.IsNullOrEmpty(sourcePath)) return;

            if (string.Equals(Path.GetFullPath(sourcePath), Path.GetFullPath(_dbFilePath), StringComparison.OrdinalIgnoreCase))
            {
                _dialogService.ShowMessage("Das ist bereits die aktive Datenbank.");
                return;
            }

            if (!_backupService.IsValidDatabaseFile(sourcePath))
            {
                _dialogService.ShowError("Die gewählte Datei ist keine gültige KerwaKasse-Datenbank.");
                return;
            }

            var summary = _backupService.GetSummary();
            if (!summary.IsEmpty)
            {
                string message = "Die aktuelle Datenbank enthält bereits Daten:\n" + SummaryText(summary);
                string warning = "Beim Wiederherstellen wird die aktuelle Datenbank unwiderruflich durch die gewählte Sicherung ersetzt.";
                if (!await _dialogService.ShowWarningConfirmationAsync("Datenbank wiederherstellen", message, warning, "Trotzdem fortfahren?"))
                    return;
            }

            try
            {
                _backupService.Restore(sourcePath);
                _dialogService.ShowMessage("Datenbank erfolgreich wiederhergestellt.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Restore from {SourcePath} failed", sourcePath);
                _dialogService.ShowError("Wiederherstellen fehlgeschlagen: " + ex.Message);
            }
        }

        private static string SummaryText(DatabaseSummary s)
        {
            var parts = new List<string>();
            if (s.Products > 0) parts.Add(Count(s.Products, "Produkt", "Produkte"));
            if (s.Orders > 0) parts.Add(Count(s.Orders, "Verkauf", "Verkäufe"));
            if (s.Menus > 0) parts.Add(Count(s.Menus, "Speisekarte", "Speisekarten"));
            if (s.SavedAnalyses > 0) parts.Add(Count(s.SavedAnalyses, "Auswertung", "Auswertungen"));
            return "• " + string.Join("\n• ", parts);
        }

        private static string Count(int n, string singular, string plural) => $"{n} {(n == 1 ? singular : plural)}";

        // Opens the AppData folder that holds the live database in the file explorer.
        private void OpenDataFolder()
        {
            try
            {
                if (!Directory.Exists(_dataFolder))
                {
                    _dialogService.ShowError("Datenordner wurde nicht gefunden.");
                    return;
                }

                Process.Start(new ProcessStartInfo
                {
                    FileName = _dataFolder,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to open data folder {DataFolder}", _dataFolder);
                _dialogService.ShowError("Datenordner konnte nicht geöffnet werden: " + ex.Message);
            }
        }
    }
}
