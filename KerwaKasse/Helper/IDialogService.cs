using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using KerwaKasse.Core.Services;
using KerwaKasse.MVVM.ViewModel;

namespace KerwaKasse.Helper
{
    /// <summary>What the user chose in the "update available" dialog.</summary>
    public enum UpdateDialogResult
    {
        /// <summary>Download and install now (or open the release page when there is no installer asset).</summary>
        InstallNow,
        /// <summary>Never offer this particular version again.</summary>
        SkipVersion,
        /// <summary>Just close the dialog; offer the version again on the next check.</summary>
        Later
    }

    /// <summary>What the user entered in the "new event" dialog. <paramref name="BackupFilePath"/> is the
    /// (already validated) backup to import, or null for an empty event.</summary>
    public record NewEventRequest(string Name, string BackupFilePath);

    public interface IDialogService
    {
        void ShowMessage(string message);
        void ShowError(string message);
        string ShowSaveFileDialog(string filter, string title, string defaultFileName, string initialDirectory = null);

        /// <summary>Shows an open-file dialog and returns the picked path, or null when cancelled.</summary>
        string ShowOpenFileDialog(string filter, string title);

        /// <summary>Shows a modern Yes/Cancel confirmation dialog. Returns true if confirmed.</summary>
        Task<bool> ShowConfirmationAsync(string title, string message);

        /// <summary>Yes/Cancel confirmation that renders <paramref name="warning"/> in the same amber
        /// warning box used on the info page, below an optional <paramref name="message"/> and above an
        /// optional <paramref name="question"/> shown after the box. Returns true if confirmed.</summary>
        Task<bool> ShowWarningConfirmationAsync(string title, string message, string warning, string question = null);

        /// <summary>Shows the Speisekarten management dialog for the given view model.</summary>
        Task ShowMenuManagementAsync(MenuManagementViewModel viewModel);

        /// <summary>Prompts for a single line of text via a modal dialog. <paramref name="label"/> is the
        /// caption shown above the text box (e.g. "Name"). Returns null when cancelled or left empty.</summary>
        Task<string> ShowTextInputAsync(string title, string label, string initialValue);

        /// <summary>Asks for the name of a new event and whether it starts empty or from a backup file,
        /// which is picked inside the dialog and checked right away with <paramref name="isValidBackup"/>.
        /// Names already in <paramref name="existingNames"/> are refused. Returns null when cancelled.</summary>
        Task<NewEventRequest> ShowNewEventDialogAsync(IReadOnlyList<string> existingNames, Func<string, bool> isValidBackup);

        /// <summary>Confirmation for deleting the event <paramref name="eventName"/>: a prominent backup
        /// warning with a button that runs <paramref name="createBackup"/> (returns the saved file's path,
        /// or null) without leaving the dialog, and a delete button that only becomes available once the
        /// event's name has been typed in. <paramref name="contents"/> describes what the event holds.
        /// Returns true if the deletion was confirmed.</summary>
        Task<bool> ShowDeleteEventDialogAsync(string eventName, string contents, Func<string> createBackup);

        /// <summary>Shows the saved-analyses management dialog for the given view model.</summary>
        Task ShowSavedAnalysisManagementAsync(SavedAnalysisManagementViewModel viewModel);

        /// <summary>Tells the user that <paramref name="update"/> is available (with its release notes)
        /// and lets them install now, skip this version or decide later.</summary>
        Task<UpdateDialogResult> ShowUpdateAvailableAsync(UpdateInfo update, string installedVersion);

        /// <summary>Runs <paramref name="download"/> behind a modal progress dialog whose only button
        /// cancels it. Returns the downloaded file's path, or null when the user cancelled.</summary>
        Task<string> ShowDownloadProgressAsync(string title, Func<IProgress<double>, CancellationToken, Task<string>> download);
    }
}
