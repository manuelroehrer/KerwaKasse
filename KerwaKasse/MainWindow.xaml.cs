using KerwaKasse.Helper;
using KerwaKasse.MVVM.ViewModel;
using KerwaKasse.Core.Data;
using Microsoft.Extensions.Logging;
using System.Windows;
using System.Windows.Input;
using System;
using System.IO;
using System.Linq;

namespace KerwaKasse
{
    public partial class MainWindow : Window
    {
        private const string WindowStateSettingKey = "MainWindow.WindowState";
        private readonly JsonSettingsService _settingsService;
        private readonly EventCatalog _eventCatalog;
        private readonly DialogService _dialogService = new();

        public MainWindow(ILoggerFactory loggerFactory, JsonSettingsService settingsService, EventCatalog eventCatalog)
        {
            _eventCatalog = eventCatalog;
            string dbFilePath = eventCatalog.ActiveFilePath;
            string connectionString = $"Data Source={dbFilePath}";

            var productService = new SqliteProductService(connectionString, loggerFactory.CreateLogger<SqliteProductService>());
            var orderService = new SqliteOrderService(connectionString, loggerFactory.CreateLogger<SqliteOrderService>());
            var dialogService = _dialogService;
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
            Loaded += async (_, _) =>
            {
                ReportStartupEventChanges();
                await mainViewModel.InfoVM.CheckForUpdatesOnStartupAsync();
            };
        }

        // Anything unusual the event catalog had to do at startup is said right away, before anyone
        // starts selling, so nothing is booked into an unexpected event unnoticed.
        private void ReportStartupEventChanges()
        {
            var fallback = _eventCatalog.StartupFallback;
            if (fallback != null)
            {
                var active = _eventCatalog.GetAll().FirstOrDefault(e =>
                    string.Equals(e.FilePath, _eventCatalog.ActiveFilePath, StringComparison.OrdinalIgnoreCase));
                string opened = fallback.NewEventCreated
                    ? $"Da keine weitere Veranstaltung vorhanden war, wurde die neue, leere Veranstaltung „{active?.Name}“ angelegt."
                    : $"Stattdessen wurde die Veranstaltung „{active?.Name}“ geöffnet.";
                _dialogService.ShowMessage(
                    $"Die zuletzt verwendete Veranstaltung wurde nicht gefunden (Datei „{fallback.MissingFileName}“).\n\n{opened}");
            }

            if (_eventCatalog.AdoptedLegacyEventName is string adopted)
            {
                _dialogService.ShowMessage(
                    "In den Anwendungsdaten wurde eine Datenbank einer älteren KerwaKasse-Version gefunden. " +
                    $"Sie wurde als zusätzliche Veranstaltung „{adopted}“ übernommen; die aktive Veranstaltung bleibt unverändert.");
            }
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
