using KerwaKasse.Core.Services;
using KerwaKasse.Helper;
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
        public RelayCommand BackupCommand { get; }
        public RelayCommand RestoreCommand { get; }
        public RelayCommand OpenDataFolderCommand { get; }

        // App name, version and copyright are read from the assembly so they
        // stay in sync with the .csproj and never need to be maintained twice.
        public string Version { get; }
        public string Copyright { get; }

        private readonly IDialogService _dialogService;
        private readonly IDatabaseBackupService _backupService;
        private readonly string _dbFilePath;
        private readonly string _dataFolder;

        public InfoViewModel(IDialogService dialogService, string dbFilePath, IDatabaseBackupService backupService)
        {
            _dialogService = dialogService;
            _backupService = backupService;
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
                    _dialogService.ShowMessage("Backup erfolgreich erstellt.");
                }
                catch (Exception ex)
                {
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
                _dialogService.ShowError("Datenordner konnte nicht geöffnet werden: " + ex.Message);
            }
        }
    }
}
