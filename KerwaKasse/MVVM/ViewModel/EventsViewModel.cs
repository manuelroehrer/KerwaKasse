using KerwaKasse.Core.Data;
using KerwaKasse.Core.Models;
using KerwaKasse.Core.Services;
using KerwaKasse.Helper;
using KerwaKasse.MVVM.Model;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace KerwaKasse.MVVM.ViewModel
{
    /// <summary>The event (Veranstaltung) selector on the info page: shows the active event, switches to
    /// another one and creates, renames, backs up and deletes events (one database file each).</summary>
    public class EventsViewModel : PropertyChangedBase
    {
        private readonly IEventCatalog _catalog;
        private readonly IDatabaseBackupService _backupService;
        private readonly IDialogService _dialogService;
        private readonly ILogger<EventsViewModel> _logger;

        public ObservableCollection<EventListItem> Events { get; } = new();

        private EventListItem _activeEvent;
        /// <summary>The event the till currently books into.</summary>
        public EventListItem ActiveEvent
        {
            get => _activeEvent;
            private set { _activeEvent = value; OnPropertyChanged(); OnPropertyChanged(nameof(ActiveEventName)); }
        }

        public string ActiveEventName => ActiveEvent?.Name ?? string.Empty;

        public RelayCommand SwitchCommand { get; }
        public RelayCommand CreateCommand { get; }
        public RelayCommand RenameCommand { get; }
        public RelayCommand DeleteCommand { get; }

        /// <summary>Raised after another event became the active one. The owner has to reconnect every
        /// service to <see cref="IEventCatalog.ActiveFilePath"/>.</summary>
        public event EventHandler ActiveEventSwitched;

        /// <summary>Raised after an event was created, renamed or deleted (e.g. for the window title).</summary>
        public event EventHandler EventsChanged;

        public EventsViewModel(IEventCatalog catalog, IDatabaseBackupService backupService, IDialogService dialogService, ILogger<EventsViewModel> logger)
        {
            _catalog = catalog;
            _backupService = backupService;
            _dialogService = dialogService;
            _logger = logger;

            SwitchCommand = new RelayCommand(async o => await SwitchAsync(o as EventListItem));
            CreateCommand = new RelayCommand(async o => await CreateAsync());
            RenameCommand = new RelayCommand(async o => await RenameAsync(o as EventListItem));
            DeleteCommand = new RelayCommand(async o => await DeleteAsync(o as EventListItem));

            Refresh();
        }

        /// <summary>Reloads names and key figures. Called whenever the info page is shown, since products
        /// and sales change on the other pages.</summary>
        public void Refresh()
        {
            Events.Clear();
            foreach (var evt in _catalog.GetAll())
                Events.Add(new EventListItem(evt, IsActive(evt.FilePath)));
            ActiveEvent = Events.FirstOrDefault(e => e.IsActive);
        }

        private bool IsActive(string filePath) =>
            string.Equals(Path.GetFullPath(filePath), Path.GetFullPath(_catalog.ActiveFilePath), StringComparison.OrdinalIgnoreCase);

        /// <summary>Asks for a target file and copies the event's database there. Returns the saved path,
        /// or null when cancelled or failed (the failure has been reported to the user already).</summary>
        public string SaveBackup(EventListItem item)
        {
            if (item is null) return null;

            string defaultFileName = $"kerwakasse_backup_{EventCatalog.Slugify(item.Name)}_{DateTime.Now:dd.MM.yyyy_HH.mm}";
            string targetPath = _dialogService.ShowSaveFileDialog(
                "SQLite Datenbank (*.db)|*.db",
                $"Speicherort für das Backup von „{item.Name}“ wählen ...",
                defaultFileName);
            if (targetPath is null) return null;

            try
            {
                File.Copy(item.FilePath, targetPath, overwrite: true);
                _logger.LogInformation("Backup of event {FilePath} created: {TargetPath}", item.FilePath, targetPath);
                return targetPath;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Backup of event {FilePath} to {TargetPath} failed", item.FilePath, targetPath);
                _dialogService.ShowError("Backup fehlgeschlagen: " + ex.Message);
                return null;
            }
        }

        private async Task SwitchAsync(EventListItem item)
        {
            if (item is null || item.IsActive) return;

            string message = $"Zur Veranstaltung „{item.Name}“ wechseln?\n\n" +
                             "Anschließend werden alle Verkäufe in dieser Veranstaltung erfasst. Produkte, Speisekarten, " +
                             "Verlauf und Auswertung beziehen sich dann ebenfalls auf diese Veranstaltung.";
            if (await _dialogService.ShowConfirmationAsync("Veranstaltung wechseln", message))
                SwitchTo(item.FilePath);
        }

        private void SwitchTo(string filePath)
        {
            try
            {
                _catalog.SetActive(filePath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Switching to event {FilePath} failed", filePath);
                _dialogService.ShowError("Die Veranstaltung konnte nicht geöffnet werden: " + ex.Message);
                return;
            }

            Refresh();
            ActiveEventSwitched?.Invoke(this, EventArgs.Empty);
        }

        private async Task CreateAsync()
        {
            var request = await _dialogService.ShowNewEventDialogAsync(
                Events.Select(e => e.Name).ToList(), _backupService.IsValidDatabaseFile);
            if (request is null || IsNameTaken(request.Name, exceptFilePath: null)) return;

            EventDatabase created;
            try
            {
                // The dialog has already checked the backup file.
                created = request.BackupFilePath is null
                    ? _catalog.Create(request.Name)
                    : _catalog.Import(request.BackupFilePath, request.Name);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Creating event \"{EventName}\" failed", request.Name);
                _dialogService.ShowError("Die Veranstaltung konnte nicht angelegt werden: " + ex.Message);
                return;
            }

            Refresh();
            EventsChanged?.Invoke(this, EventArgs.Empty);

            if (await _dialogService.ShowConfirmationAsync("Veranstaltung wechseln",
                    $"Die Veranstaltung „{created.Name}“ wurde angelegt. Soll direkt zu ihr gewechselt werden?"))
                SwitchTo(created.FilePath);
        }

        private async Task RenameAsync(EventListItem item)
        {
            if (item is null) return;

            string name = await _dialogService.ShowTextInputAsync("Veranstaltung umbenennen", "Name", item.Name);
            if (name is null || name == item.Name || IsNameTaken(name, exceptFilePath: item.FilePath)) return;

            try
            {
                _catalog.Rename(item.FilePath, name);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Renaming event {FilePath} failed", item.FilePath);
                _dialogService.ShowError("Die Veranstaltung konnte nicht umbenannt werden: " + ex.Message);
                return;
            }

            Refresh();
            EventsChanged?.Invoke(this, EventArgs.Empty);
        }

        private async Task DeleteAsync(EventListItem item)
        {
            if (item is null || item.IsActive) return;

            // No full stop right after the closing parenthesis of the date range.
            string contents = $"„{item.Name}“ enthält {item.ContentsText}";
            if (!contents.EndsWith(')'))
                contents += ".";
            if (!await _dialogService.ShowDeleteEventDialogAsync(item.Name, contents, () => SaveBackup(item)))
                return;

            try
            {
                _catalog.Delete(item.FilePath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Deleting event {FilePath} failed", item.FilePath);
                _dialogService.ShowError("Die Veranstaltung konnte nicht gelöscht werden: " + ex.Message);
            }

            Refresh();
            EventsChanged?.Invoke(this, EventArgs.Empty);
        }

        // Two events with the same name would be indistinguishable in the selector and the window title.
        private bool IsNameTaken(string name, string exceptFilePath)
        {
            bool taken = Events.Any(e => string.Equals(e.Name.Trim(), name.Trim(), StringComparison.CurrentCultureIgnoreCase)
                                         && !string.Equals(e.FilePath, exceptFilePath, StringComparison.OrdinalIgnoreCase));
            if (taken)
                _dialogService.ShowError($"Es existiert bereits eine Veranstaltung mit dem Namen „{name.Trim()}“.");
            return taken;
        }
    }
}
