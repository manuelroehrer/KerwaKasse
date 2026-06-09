using System.Threading.Tasks;

namespace KerwaKasse.Helper
{
    public interface IDialogService
    {
        void ShowMessage(string message);
        void ShowError(string message);
        string ShowSaveFileDialog(string filter, string title, string defaultFileName);

        /// <summary>Shows a modern Yes/Cancel confirmation dialog. Returns true if confirmed.</summary>
        Task<bool> ShowConfirmationAsync(string title, string message);
    }
}
