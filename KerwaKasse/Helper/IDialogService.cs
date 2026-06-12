using System.Threading.Tasks;
using KerwaKasse.MVVM.ViewModel;

namespace KerwaKasse.Helper
{
    public interface IDialogService
    {
        void ShowMessage(string message);
        void ShowError(string message);
        string ShowSaveFileDialog(string filter, string title, string defaultFileName);

        /// <summary>Shows a modern Yes/Cancel confirmation dialog. Returns true if confirmed.</summary>
        Task<bool> ShowConfirmationAsync(string title, string message);

        /// <summary>Shows the Speisekarten management dialog for the given view model.</summary>
        Task ShowMenuManagementAsync(MenuManagementViewModel viewModel);
    }
}
