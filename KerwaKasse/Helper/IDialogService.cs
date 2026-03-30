namespace KerwaKasse.Helper
{
    public interface IDialogService
    {
        void ShowMessage(string message);
        void ShowError(string message);
        string ShowSaveFileDialog(string filter, string title, string defaultFileName);
    }
}
