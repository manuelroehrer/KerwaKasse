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
#if DEBUG
        private const string SimulatedVersionVariable = "KERWAKASSE_SIMULATED_VERSION";
#endif

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

        /// <summary>The event selector at the top of the "Veranstaltung" card. Backup and restore below it
        /// always act on its active event.</summary>
        public EventsViewModel Events { get; }

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
        private readonly string _dataFolder;
        private readonly Version _currentVersion;
        private readonly string _currentVersionText;

        public InfoViewModel(IDialogService dialogService, string dataFolder, IDatabaseBackupService backupService, IUpdateService updateService, ISettingsService settingsService, EventsViewModel events, ILogger<InfoViewModel> logger)
        {
            Events = events;
            _dialogService = dialogService;
            _backupService = backupService;
            _updateService = updateService;
            _settingsService = settingsService;
            _logger = logger;
            _dataFolder = dataFolder;

            var assembly = Assembly.GetExecutingAssembly();
            _currentVersion = assembly.GetName().Version ?? new Version(0, 0, 0);
#if DEBUG
            // Lets a debug session pretend to be an older version, so the update dialog can be tried
            // against the real GitHub releases. Set by the "update test" profile in launchSettings.json.
            if (System.Version.TryParse(Environment.GetEnvironmentVariable(SimulatedVersionVariable), out var simulatedVersion))
            {
                _logger.LogInformation("Simulating version {Version} for the update check ({Variable})", simulatedVersion, SimulatedVersionVariable);
                _currentVersion = simulatedVersion;
            }
#endif
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

                _logger.LogInformation("Starting installer for update {Version}, shutting down", update.Version);

                // Run the installer silently; /AUTORELAUNCH=1 tells it to start KerwaKasse again once
                // the update is applied. Shutting down here lets the app close normally: the installer
                // waits for this process to exit before it replaces any files, so the shutdown
                // (settings written, database handles released, log flushed) always finishes first.
                Process.Start(new ProcessStartInfo(installerPath, "/SILENT /AUTORELAUNCH=1")
                {
                    UseShellExecute = true
                });
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
            if (Events.SaveBackup(Events.ActiveEvent) != null)
                _dialogService.ShowMessage("Backup erfolgreich erstellt.");
        }

        // Replaces the active event's data with a chosen backup file. An empty (never-used) event is replaced
        // straight away; one that already holds data needs an explicit confirmation first, with the
        // counts spelled out and a reminder that the step is irreversible. The event keeps its name.
        private async Task RestoreDatabaseAsync()
        {
            string eventName = Events.ActiveEventName;
            string sourcePath = _dialogService.ShowOpenFileDialog(
                "SQLite Datenbank (*.db)|*.db|Alle Dateien (*.*)|*.*",
                $"Sicherung zum Wiederherstellen von „{eventName}“ wählen ...");
            if (string.IsNullOrEmpty(sourcePath)) return;

            if (string.Equals(Path.GetFullPath(sourcePath), Path.GetFullPath(Events.ActiveEvent?.FilePath ?? string.Empty), StringComparison.OrdinalIgnoreCase))
            {
                _dialogService.ShowMessage("Die gewählte Datei ist die Datenbank der aktiven Veranstaltung selbst.");
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
                string message = $"„{eventName}“ enthält bereits Daten:\n" + SummaryText(summary);
                string warning = $"Beim Wiederherstellen werden die Daten von „{eventName}“ unwiderruflich durch die gewählte Sicherung ersetzt.";
                if (!await _dialogService.ShowWarningConfirmationAsync("Veranstaltung wiederherstellen", message, warning, "Trotzdem fortfahren?"))
                    return;
            }

            try
            {
                _backupService.Restore(sourcePath);
                Events.Refresh();
                _dialogService.ShowMessage($"Die Veranstaltung „{eventName}“ wurde erfolgreich wiederhergestellt.");
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
