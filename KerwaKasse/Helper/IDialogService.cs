using System.Threading.Tasks;
using KerwaKasse.MVVM.ViewModel;

namespace KerwaKasse.Helper
{
    public interface IDialogService
    {
        void ShowMessage(string message);
        void ShowError(string message);
        string ShowSaveFileDialog(string filter, string title, string defaultFileName, string initialDirectory = null);

        /// <summary>Shows a modern Yes/Cancel confirmation dialog. Returns true if confirmed.</summary>
        Task<bool> ShowConfirmationAsync(string title, string message);

        /// <summary>Shows the Speisekarten management dialog for the given view model.</summary>
        Task ShowMenuManagementAsync(MenuManagementViewModel viewModel);

        /// <summary>Prompts for a single line of text via a modal dialog. <paramref name="label"/> is the
        /// caption shown above the text box (e.g. "Name"). Returns null when cancelled or left empty.</summary>
        Task<string> ShowTextInputAsync(string title, string label, string initialValue);

        /// <summary>Shows the saved-analyses management dialog for the given view model.</summary>
        Task ShowSavedAnalysisManagementAsync(SavedAnalysisManagementViewModel viewModel);
    }
}
