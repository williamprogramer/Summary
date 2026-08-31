using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Serilog;
using SQLitePCL;
using Summary.Data;
using Summary.Helpers;
using Summary.Services.Whisper;
using System;
using System.Threading.Tasks;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace Summary
{
    /// <summary>
    /// Provides application-specific behavior to supplement the default Application class.
    /// </summary>
    public partial class App : Application
    {
        private Window? _window;
        public static IServiceProvider ServiceProvider { get; private set; } = default!;

        /// <summary>
        /// Gets the dispatcher queue for the application.
        /// </summary>
        public static DispatcherQueue DispatcherQueue { get; private set; } = default!;

        /// <summary>
        /// Initializes the singleton application object.  This is the first line of authored code
        /// executed, and as such is the logical equivalent of main() or WinMain().
        /// </summary>
        public App()
        {
            InitializeComponent();
            UnhandledException += App_UnhandledException;
            try
            {
                Batteries_V2.Init();
                DispatcherQueue = DispatcherQueue.GetForCurrentThread();
                PathHelper.EnsureDirectories();
                Startup.ConfigureLogging();
                ServiceProvider = Startup.ConfigureServices();
                Log.Information("Application initialized successfully.");
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "Application failed to initialize.");
            }
        }

        /// <summary>
        /// Invoked when the application is launched.
        /// </summary>
        /// <param name="args">Details about the launch request and process.</param>
        protected override void OnLaunched(LaunchActivatedEventArgs args)
        {
            try
            {
                IDbContextFactory<SummaryDBContext> dbContextFactory = ServiceProvider.GetRequiredService<IDbContextFactory<SummaryDBContext>>();
                using SummaryDBContext context = dbContextFactory.CreateDbContext();
                context.Database.EnsureCreated();
                Log.Information("Database created.");
                _window = ServiceProvider.GetRequiredService<MainWindow>();
                _window.Activate();
                Log.Information("Application launched successfully.");
                _ = EnsureWhisperModelAsync();
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "Application failed to launch.");
                throw;
            }
        }

        private static async Task EnsureWhisperModelAsync()
        {
            try
            {
                WhisperModelDownloadService downloader = ServiceProvider.GetRequiredService<WhisperModelDownloadService>();
                await Task.Run(async () => await downloader.EnsureModelsAsync().ConfigureAwait(false));
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Whisper model download failed.");
            }
        }

        /// <summary>
        /// Invoked when an unhandled exception occurs in the application.
        /// </summary>
        /// <param name="sender">The source of the unhandled exception.</param>
        /// <param name="e">Details about the unhandled exception.</param>
        private void App_UnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
        {
            Log.Error(e.Exception, "Unhandled exception occurred.");
            e.Handled = true;
        }
    }
}