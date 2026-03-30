using KerwaKasse.Helper;
using System;
using System.IO;

namespace KerwaKasse.MVVM.ViewModel
{
    public class DatabaseViewModel : PropertyChangedBase
    {
        public RelayCommand BackupCommand { get; }

        private readonly IDialogService _dialogService;
        private readonly string _dbFilePath;

        public DatabaseViewModel(IDialogService dialogService, string dbFilePath)
        {
            _dialogService = dialogService;
            _dbFilePath = dbFilePath;
            BackupCommand = new RelayCommand(o => BackupDatabase());
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
    }
}
