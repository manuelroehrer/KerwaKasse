using System;
using System.IO;
using KerwaKasse.Core.Services;

namespace KerwaKasse.Helper
{
    /// <summary>
    /// Where a save dialog starts and which file name it suggests: the folder something was saved to
    /// last (kept in the settings, one for all save dialogs), or Documents when there is none or it no
    /// longer exists; and a name that is still free in that folder.
    /// </summary>
    public class SaveLocation
    {
        private const string LastFolderKey = "lastSaveFolder";

        private readonly ISettingsService _settings;

        public SaveLocation(ISettingsService settings)
        {
            _settings = settings;
        }

        /// <summary>The folder the save dialog opens in.</summary>
        public string InitialFolder()
        {
            string last = _settings.Get<string>(LastFolderKey);
            return !string.IsNullOrEmpty(last) && Directory.Exists(last)
                ? last
                : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        }

        /// <summary>Remembers the folder of a file just saved, for the next save dialog.</summary>
        public void Remember(string filePath)
        {
            string folder = Path.GetDirectoryName(filePath);
            if (string.IsNullOrEmpty(folder)) return;

            _settings.Set(LastFolderKey, folder);
            _settings.Save();
        }

        /// <summary>A file name (no path) that does not yet exist in <paramref name="folder"/>:
        /// "<paramref name="baseName"/><paramref name="extension"/>", else with " (2)", " (3)", … appended.</summary>
        public static string UniqueFileName(string folder, string baseName, string extension)
        {
            string candidate = baseName + extension;
            int n = 2;
            while (File.Exists(Path.Combine(folder, candidate)))
                candidate = $"{baseName} ({n++}){extension}";
            return candidate;
        }
    }
}
