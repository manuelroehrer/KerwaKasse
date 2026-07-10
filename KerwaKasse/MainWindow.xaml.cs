using KerwaKasse.Helper;
using KerwaKasse.MVVM.ViewModel;
using KerwaKasse.Core.Data;
using Microsoft.Extensions.Logging;
using System.Windows;
using System.Windows.Input;
using System;
using System.IO;

namespace KerwaKasse
{
    public partial class MainWindow : Window
    {
        private const string WindowStateSettingKey = "MainWindow.WindowState";
        private readonly JsonSettingsService _settingsService;

        public MainWindow(ILoggerFactory loggerFactory)
        {
            string appDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "KerwaKasse");
            string dbFilePath = Path.Combine(appDir, "kerwakasse.db");
            string settingsFilePath = Path.Combine(appDir, "settings.json");
            string connectionString = $"Data Source={dbFilePath}";

            var productService = new SqliteProductService(connectionString, loggerFactory.CreateLogger<SqliteProductService>());
            var orderService = new SqliteOrderService(connectionString, loggerFactory.CreateLogger<SqliteOrderService>());
            var settingsService = new JsonSettingsService(settingsFilePath, loggerFactory.CreateLogger<JsonSettingsService>());
            var dialogService = new DialogService();
            var menuService = new SqliteMenuService(connectionString, loggerFactory.CreateLogger<SqliteMenuService>());
            var analyticsService = new SqliteAnalyticsService(connectionString);
            var savedAnalysisService = new SqliteSavedAnalysisService(connectionString, loggerFactory.CreateLogger<SqliteSavedAnalysisService>());
            var backupService = new DatabaseBackupService(connectionString, dbFilePath, loggerFactory.CreateLogger<DatabaseBackupService>());
            var updateService = new GitHubUpdateService(InfoViewModel.RepositoryUrl, loggerFactory.CreateLogger<GitHubUpdateService>());
            _settingsService = settingsService;

            var mainViewModel = new MainWindowViewModel(dialogService, productService, orderService, settingsService, dbFilePath, menuService, analyticsService, savedAnalysisService, backupService, updateService, loggerFactory);
            DataContext = mainViewModel;
            InitializeComponent();
            RestoreWindowState();

            // The background update check waits until the window is up; it never blocks the UI and
            // only ever speaks up when a new version was actually found.
            Loaded += async (_, _) => await mainViewModel.InfoVM.CheckForUpdatesOnStartupAsync();
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

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            SaveWindowState();
            base.OnClosing(e);
        }

        private void RestoreWindowState()
        {
            var savedState = _settingsService.Get<string>(WindowStateSettingKey);
            WindowState = string.Equals(savedState, nameof(WindowState.Maximized), StringComparison.Ordinal)
                ? WindowState.Maximized
                : WindowState.Normal;
        }

        private void SaveWindowState()
        {
            var normalizedState = WindowState == WindowState.Maximized
                ? nameof(WindowState.Maximized)
                : nameof(WindowState.Normal);

            _settingsService.Set(WindowStateSettingKey, normalizedState);
            _settingsService.Save();
        }
    }
}
