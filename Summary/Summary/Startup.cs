using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using Summary.Data;
using Summary.Helpers;
using Summary.Services;
using Summary.Services.Whisper;
using Summary.ViewModels;
using System.IO;

namespace Summary
{
    internal static class Startup
    {
        /// <summary>
        /// Configures the services for the application and returns a ServiceProvider.
        /// </summary>
        /// <returns>The configured ServiceProvider.</returns>
        public static ServiceProvider ConfigureServices()
        {
            ServiceCollection services = new();
            services.AddHttpClient();
            services.AddSingleton<MainWindow>();
            services.AddTransient<SettingsViewModel>();
            services.AddTransient<DefaultViewModel>();
            services.AddTransient<SettingsService>();
            services.AddSingleton<BusyService>();
            services.AddSingleton<WhisperOnnxTranscriber>();
            services.AddTransient<NAudioService>();
            services.AddLogging(configure => configure.AddSerilog());
            services.AddDbContextFactory<SummaryDBContext>(options =>
            {
                options.UseSqlite($"Data Source={Path.Combine(PathHelper.DatabasePath, "summary.db")}");
            });
            return services.BuildServiceProvider();
        }

        /// <summary>
        /// Configures Serilog logging for the application, setting the minimum log level to Debug and writing logs to a file with daily rolling intervals.
        /// </summary>
        public static void ConfigureLogging()
        {
            Log.Logger = new LoggerConfiguration()
#if DEBUG
                .MinimumLevel.Debug()
#else
                .MinimumLevel.Information()
#endif
                .WriteTo.File(Path.Combine(PathHelper.LogsPath, "summary.log"), rollingInterval: RollingInterval.Day)
                .CreateLogger();
        }
    }
}