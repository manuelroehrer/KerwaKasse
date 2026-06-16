using KerwaKasse.Helper;
using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;

namespace KerwaKasse.MVVM.ViewModel
{
    public class InfoViewModel : PropertyChangedBase
    {
        public RelayCommand BackupCommand { get; }
        public RelayCommand OpenDataFolderCommand { get; }

        // App name, version and copyright are read from the assembly so they
        // stay in sync with the .csproj and never need to be maintained twice.
        public string Version { get; }
        public string Copyright { get; }

        private readonly IDialogService _dialogService;
        private readonly string _dbFilePath;
        private readonly string _dataFolder;

        public InfoViewModel(IDialogService dialogService, string dbFilePath)
        {
            _dialogService = dialogService;
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
