using KerwaKasse.Helper;
using KerwaKasse.MVVM.ViewModel;
using KerwaKasse.Core.Data;
using System.Windows;
using System;
using System.IO;

namespace KerwaKasse
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            string appDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "KerwaKasse");
            string dbFilePath = Path.Combine(appDir, "kerwakasse.db");
            string settingsFilePath = Path.Combine(appDir, "settings.json");
            string connectionString = $"Data Source={dbFilePath}";

            var productService = new SqliteProductService(connectionString);
            var orderService = new SqliteOrderService(connectionString);
            var settingsService = new JsonSettingsService(settingsFilePath);
            var dialogService = new DialogService();

            DataContext = new MainWindowViewModel(dialogService, productService, orderService, settingsService, dbFilePath);
            InitializeComponent();
        }
    }
}
