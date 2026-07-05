using KerwaKasse.Core.Services;
using KerwaKasse.Helper;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;

namespace KerwaKasse.MVVM.ViewModel
{
    public class InfoViewModel : PropertyChangedBase
    {
        private const string RepositoryUrl = "https://github.com/manuelroehrer/KerwaKasse";

        public RelayCommand BackupCommand { get; }
        public RelayCommand RestoreCommand { get; }
        public RelayCommand OpenDataFolderCommand { get; }
        public RelayCommand OpenRepositoryCommand { get; }
        public RelayCommand OpenThirdPartyNoticesCommand { get; }

        // App name, version and copyright are read from the assembly so they
        // stay in sync with the .csproj and never need to be maintained twice.
        public string Version { get; }
        public string Copyright { get; }

        private readonly IDialogService _dialogService;
        private readonly IDatabaseBackupService _backupService;
        private readonly ILogger<InfoViewModel> _logger;
        private readonly string _dbFilePath;
        private readonly string _dataFolder;

        public InfoViewModel(IDialogService dialogService, string dbFilePath, IDatabaseBackupService backupService, ILogger<InfoViewModel> logger)
        {
            _dialogService = dialogService;
            _backupService = backupService;
            _logger = logger;
            _dbFilePath = dbFilePath;
            _dataFolder = Path.GetDirectoryName(dbFilePath) ?? string.Empty;

            var assembly = Assembly.GetExecutingAssembly();
            var version = assembly.GetName().Version;
            Version = version != null
                ? $"Version {version.Major}.{version.Minor}.{version.Build}"
                : "Version unbekannt";
            Copyright = assembly.GetCustomAttribute<AssemblyCopyrightAttribute>()?.Copyright
                        ?? "© Manuel Röhrer";

            BackupCommand = new RelayCommand(o => BackupDatabase());
            RestoreCommand = new RelayCommand(async o => await RestoreDatabaseAsync());
            OpenDataFolderCommand = new RelayCommand(o => OpenDataFolder());
            OpenRepositoryCommand = new RelayCommand(o => OpenRepository());
            OpenThirdPartyNoticesCommand = new RelayCommand(o => OpenThirdPartyNotices());
        }

        private void OpenRepository()
        {
            try
            {
                Process.Start(new ProcessStartInfo(RepositoryUrl) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to open repository URL");
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
