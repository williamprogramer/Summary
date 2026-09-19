using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using Summary.Data;
using Summary.Helpers;
using Summary.Services;
using Summary.Services.Translation;
using Summary.Services.Whisper;
using Summary.ViewModels;
using System;
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
            services.AddHttpClient(WhisperModelDownloadService.HttpClientName, client =>
            {
                client.Timeout = TimeSpan.FromHours(2);
                client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "Summary/1.0");
            });
            services.AddSingleton<MainWindow>();
            services.AddTransient<SettingsViewModel>();
            services.AddTransient<DefaultViewModel>();
            services.AddTransient<LiveTranslationViewModel>();
            services.AddTransient<SettingsService>();
            services.AddSingleton<BusyService>();
            services.AddSingleton<HuggingFaceModelDownloadService>();
            services.AddSingleton<WhisperModelDownloadService>();
            services.AddSingleton<LiveModelDownloadService>();
            services.AddKeyedSingleton<WhisperOnnxTranscriber>(WhisperModelKeys.Large, (sp, _) =>
                new WhisperOnnxTranscriber(sp.GetRequiredService<ILogger<WhisperOnnxTranscriber>>(), PathHelper.WhisperLargePath));
            services.AddKeyedSingleton<WhisperOnnxTranscriber>(WhisperModelKeys.Small, (sp, _) =>
                new WhisperOnnxTranscriber(sp.GetRequiredService<ILogger<WhisperOnnxTranscriber>>(), PathHelper.WhisperSmallPath));
            services.AddSingleton<OpusMtTranslator>();
            services.AddTransient<LiveTranslationPipeline>(sp =>
                new LiveTranslationPipeline(
                    sp.GetRequiredKeyedService<WhisperOnnxTranscriber>(WhisperModelKeys.Small),
                    sp.GetRequiredService<OpusMtTranslator>(),
                    sp.GetRequiredService<ILogger<LiveTranslationPipeline>>()));
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