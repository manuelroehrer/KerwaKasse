using System.Globalization;
using System.Windows;
using System.Windows.Markup;
using KerwaKasse.Core.Data;
using System;
using System.IO;

namespace KerwaKasse
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            FrameworkElement.LanguageProperty.OverrideMetadata(
                typeof(FrameworkElement),
                new FrameworkPropertyMetadata(XmlLanguage.GetLanguage(CultureInfo.CurrentCulture.IetfLanguageTag)));

            string appDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "KerwaKasse");
            Directory.CreateDirectory(appDir);
            string dbFilePath = Path.Combine(appDir, "kerwakasse.db");
            string connectionString = $"Data Source={dbFilePath}";

            DatabaseInitializer.Initialize(connectionString);

            base.OnStartup(e);
        }
    }
}
