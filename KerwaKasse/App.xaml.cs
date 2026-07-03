using System.Globalization;
using System.Reflection;
using System.Windows;
using System.Windows.Markup;
using KerwaKasse.Core.Data;
using System;
using System.IO;
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

        protected override void OnStartup(StartupEventArgs e)
        {
            FrameworkElement.LanguageProperty.OverrideMetadata(
                typeof(FrameworkElement),
                new FrameworkPropertyMetadata(XmlLanguage.GetLanguage(CultureInfo.CurrentCulture.IetfLanguageTag)));

            string appDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "KerwaKasse");
            Directory.CreateDirectory(appDir);
            string dbFilePath = Path.Combine(appDir, "kerwakasse.db");
            string connectionString = $"Data Source={dbFilePath}";

            // Serilog writes to a daily rolling file; retainedFileCountLimit is null on purpose so
            // last year's logs survive until the next Kerwa (the app runs only a few days per year).
            // The invariant format provider keeps numbers deterministic (3.50 instead of 3,50)
            // regardless of the machine's culture.
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .WriteTo.File(
                    Path.Combine(appDir, "logs", "kerwakasse-.log"),
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: null,
                    outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}",
                    formatProvider: CultureInfo.InvariantCulture)
                .CreateLogger();

            _loggerFactory = new SerilogLoggerFactory(Log.Logger);
            _logger = _loggerFactory.CreateLogger<App>();

            var version = Assembly.GetExecutingAssembly().GetName().Version;
            _logger.LogInformation("KerwaKasse {Version} started, database: {DbFilePath}",
                version != null ? $"{version.Major}.{version.Minor}.{version.Build}" : "unknown", dbFilePath);

            RegisterGlobalExceptionLogging();

            DatabaseInitializer.Initialize(connectionString, _loggerFactory.CreateLogger(typeof(DatabaseInitializer)));

            new MainWindow(_loggerFactory).Show();

            base.OnStartup(e);
        }

        protected override void OnExit(ExitEventArgs e)
        {
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
