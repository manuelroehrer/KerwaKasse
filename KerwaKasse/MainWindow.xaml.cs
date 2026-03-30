using KerwaKasse.Helper;
using KerwaKasse.MVVM.ViewModel;
using KerwaKasse.Core.Data;
using System.Windows;
using System.Windows.Input;
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

        private void NavButton_Click(object sender, RoutedEventArgs e)
        {
            if (Tg_Btn.IsChecked == true)
                Tg_Btn.IsChecked = false;
        }

        protected override void OnPreviewMouseDown(MouseButtonEventArgs e)
        {
            base.OnPreviewMouseDown(e);
            if (Tg_Btn.IsChecked == true)
            {
                var pos = e.GetPosition(nav_pnl);
                if (pos.X < 0 || pos.X > nav_pnl.ActualWidth || pos.Y < 0 || pos.Y > nav_pnl.ActualHeight)
                    Tg_Btn.IsChecked = false;
            }
        }
    }
}
