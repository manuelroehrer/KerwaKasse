using System.Threading.Tasks;
using KerwaKasse.MVVM.ViewModel;

namespace KerwaKasse.Helper
{
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

        /// <summary>Shows the saved-analyses management dialog for the given view model.</summary>
        Task ShowSavedAnalysisManagementAsync(SavedAnalysisManagementViewModel viewModel);
    }
}
