using Microsoft.VisualBasic.FileIO;

namespace KerwaKasse.Helper
{
    /// <summary>Deletes files by moving them to the Windows recycle bin, so an accidentally deleted event
    /// (a whole year's sales) can still be recovered.</summary>
    public static class RecycleBin
    {
        public static void SendFile(string path) =>
            FileSystem.DeleteFile(path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
    }
}
