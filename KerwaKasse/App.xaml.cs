using System.Globalization;
using System.Reflection;
using System.Windows;
using System.Windows.Markup;
using KerwaKasse.Core.Data;
using Microsoft.Data.Sqlite;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Extensions.Logging;

namespace KerwaKasse
{
    public partial class App : Application
    {
        private ILoggerFactory _loggerFactory;
        private ILogger<App> _logger;

        // Named single-instance handles. The DEBUG suffix keeps a Visual Studio debug build from
        // colliding with an installed release build during development.
#if DEBUG
        private const string SingleInstanceName = "KerwaKasse.SingleInstance.Debug";
#else
        private const string SingleInstanceName = "KerwaKasse.SingleInstance";
#endif
        private static string ActivateEventName => SingleInstanceName + ".Activate";

        private Mutex _singleInstanceMutex;   // kept alive for the whole process so it is not released early
        private EventWaitHandle _activateSignal;

        protected override void OnStartup(StartupEventArgs e)
        {
            // Enforce a single running instance: a second launch (e.g. an accidental double-click) would
            // otherwise open a second window on the same database, where a stale product list could lead
            // to wrong prices being booked. If one is already running, bring its window to the front and
            // quit right away — before the logger is set up, so no duplicate log file is created.
            _singleInstanceMutex = new Mutex(initiallyOwned: true, SingleInstanceName, out bool isFirstInstance);
            if (!isFirstInstance)
            {
                SignalExistingInstance();
                Shutdown();
                return;
            }

            // The UI is German-only for now, so pin the culture instead of following the OS:
            // otherwise date pickers and weekday names render in the OS language on non-German
            // machines while the rest of the UI stays German.
            var culture = CultureInfo.GetCultureInfo("de-DE");
            CultureInfo.DefaultThreadCurrentCulture = culture;
            CultureInfo.DefaultThreadCurrentUICulture = culture;
            FrameworkElement.LanguageProperty.OverrideMetadata(
                typeof(FrameworkElement),
                new FrameworkPropertyMetadata(XmlLanguage.GetLanguage(culture.IetfLanguageTag)));

            string appDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "KerwaKasse");
            Directory.CreateDirectory(appDir);
            string dbFilePath = Path.Combine(appDir, "kerwakasse.db");
            string connectionString = $"Data Source={dbFilePath}";

            // Serilog writes to a daily rolling file, capped at roughly a year of files as a safety
            // limit against unbounded growth; the daily files are small, so keeping that much history
            // stays cheap. The invariant format provider keeps numbers deterministic (3.50 instead of
            // 3,50) regardless of the machine's culture.
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .WriteTo.File(
                    Path.Combine(appDir, "logs", "kerwakasse-.log"),
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 365,
                    outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}",
                    formatProvider: CultureInfo.InvariantCulture)
                .CreateLogger();

            _loggerFactory = new SerilogLoggerFactory(Log.Logger);
            _logger = _loggerFactory.CreateLogger<App>();

            var version = Assembly.GetExecutingAssembly().GetName().Version;
            _logger.LogInformation("KerwaKasse {Version} started, database: {DbFilePath}",
                version != null ? $"{version.Major}.{version.Minor}.{version.Build}" : "unknown", dbFilePath);

            RegisterGlobalExceptionLogging();

            // Remove the installer a previous one-click update left behind (this run may be the
            // relaunch after such an update).
            GitHubUpdateService.CleanupDownloadedInstallers(_loggerFactory.CreateLogger(typeof(GitHubUpdateService)));

            DatabaseInitializer.Initialize(connectionString, _loggerFactory.CreateLogger(typeof(DatabaseInitializer)));

            new MainWindow(_loggerFactory).Show();

            StartActivationListener();

            base.OnStartup(e);
        }

        // Lets a second, exiting instance ask this one to surface. A background thread waits for the
        // signal and brings the main window to the front on the UI thread.
        private void StartActivationListener()
        {
            _activateSignal = new EventWaitHandle(false, EventResetMode.AutoReset, ActivateEventName);
            var listener = new Thread(() =>
            {
                while (_activateSignal.WaitOne())
                    Dispatcher.Invoke(BringMainWindowToFront);
            })
            {
                IsBackground = true,
                Name = "SingleInstanceActivationListener"
            };
            listener.Start();
        }

        private void BringMainWindowToFront()
        {
            if (MainWindow is null) return;

            if (MainWindow.WindowState == WindowState.Minimized)
                MainWindow.WindowState = WindowState.Normal;

            MainWindow.Activate();
            // Briefly forcing Topmost pulls the window to the foreground without the SetForegroundWindow
            // restrictions that stop a background process from stealing focus.
            MainWindow.Topmost = true;
            MainWindow.Topmost = false;

            _logger.LogInformation("Second launch detected; brought the existing window to the front");
        }

        // Signals the already-running instance to surface. If it has the mutex but has not published its
        // activation event yet, there is nothing to do beyond exiting quietly.
        private static void SignalExistingInstance()
        {
            try
            {
                using var signal = EventWaitHandle.OpenExisting(ActivateEventName);
                signal.Set();
            }
            catch (WaitHandleCannotBeOpenedException)
            {
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            // Pooled connections keep the database file open past their Dispose; release them so
            // SQLite closes the file cleanly instead of leaving journal remnants behind.
            SqliteConnection.ClearAllPools();

            _logger?.LogInformation("KerwaKasse exited (exit code {ExitCode})", e.ApplicationExitCode);
            Log.CloseAndFlush();
            base.OnExit(e);
        }

        // Crashes on the production laptop leave no trace otherwise; log them before the app dies.
        private void RegisterGlobalExceptionLogging()
        {
            DispatcherUnhandledException += (_, args) =>
                _logger.LogCritical(args.Exception, "Unhandled exception on UI thread");

            AppDomain.CurrentDomain.UnhandledException += (_, args) =>
                _logger.LogCritical(args.ExceptionObject as Exception,
                    "Unhandled exception (AppDomain), terminating: {IsTerminating}", args.IsTerminating);

            TaskScheduler.UnobservedTaskException += (_, args) =>
                _logger.LogError(args.Exception, "Unobserved task exception");
        }
    }
}
