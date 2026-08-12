using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Summary.Common;
using Summary.Data;
using Summary.Data.Entities;
using Summary.Models;
using Summary.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

namespace Summary.ViewModels
{
    public partial class SettingsViewModel : ObservableObject
    {
        private readonly BusyService _busy;
        private readonly ILogger<SettingsViewModel> _logger;
        private readonly IDbContextFactory<SummaryDBContext> _contextFactory;
        private readonly OllamaService _ollamaService;
        private readonly SettingsService _settingsService = default!;

        [ObservableProperty]
        public partial bool IsOllamaURLValid { get; set; } = false;
        [ObservableProperty]
        public partial string OllamaUrl { get; set; } = string.Empty;
        [ObservableProperty]
        public partial OllamaModel? SelectedOllamaModel { get; set; }
        public ObservableCollection<OllamaModel> OllamaModels { get; } = [];

        public SettingsViewModel()
        {
            _busy = App.ServiceProvider.GetRequiredService<BusyService>();
            _logger = App.ServiceProvider.GetRequiredService<ILogger<SettingsViewModel>>();
            _contextFactory = App.ServiceProvider.GetRequiredService<IDbContextFactory<SummaryDBContext>>();
            _ollamaService = App.ServiceProvider.GetRequiredService<OllamaService>();
            _settingsService = App.ServiceProvider.GetRequiredService<SettingsService>();
        }

        [RelayCommand]
        private async Task ValidateOllamaUrlAsync()
        {
            if (string.IsNullOrWhiteSpace(OllamaUrl) || IsOllamaURLValid)
                return;

            _busy.IsBusy = true;
            try
            {
                if (await _ollamaService.ValidateOllamaUrlAsync(OllamaUrl))
                {
                    List<OllamaModel> models = await _ollamaService.GetOllamaModelsAsync(OllamaUrl);
                    OllamaModels.Clear();
                    foreach (OllamaModel model in models)
                    {
                        OllamaModels.Add(model);
                    }
                    IsOllamaURLValid = true;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error validating Ollama URL.");
                IsOllamaURLValid = false;
            }
            finally
            {
                _busy.IsBusy = false;
            }
        }

        [RelayCommand]
        private async Task SaveOllamaSettingsAsync()
        {
            _busy.IsBusy = true;
            try
            {
                using SummaryDBContext context = await _contextFactory.CreateDbContextAsync();
                SettingsEntity? uri = await context.Settings.FirstOrDefaultAsync(s => s.Key == SummaryConstants.OLLAMA_URI);
                SettingsEntity? model = await context.Settings.FirstOrDefaultAsync(s => s.Key == SummaryConstants.OLLAMA_MODEL);
                uri?.Value = OllamaUrl;
                model?.Value = SelectedOllamaModel?.Name ?? string.Empty;
                await context.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving Ollama settings.");
            }
            finally
            {
                _busy.IsBusy = false;
            }
        }

        public async Task Initialize()
        {
            _busy.IsBusy = true;
            try
            {
                await _settingsService.CreateKeysIfMissingAsync();

                SettingsEntity? uri = await _settingsService.GetSettingByKeyAsync(SummaryConstants.OLLAMA_URI);
                SettingsEntity? model = await _settingsService.GetSettingByKeyAsync(SummaryConstants.OLLAMA_MODEL);

                OllamaUrl = uri?.Value ?? string.Empty;

                if (string.IsNullOrWhiteSpace(OllamaUrl))
                    return;

                IsOllamaURLValid = await _ollamaService.ValidateOllamaUrlAsync(OllamaUrl);
                if (!IsOllamaURLValid)
                    return;

                List<OllamaModel> models = await _ollamaService.GetOllamaModelsAsync(OllamaUrl);
                OllamaModels.Clear();
                foreach (OllamaModel ollamaModel in models)
                    OllamaModels.Add(ollamaModel);

                if (!string.IsNullOrEmpty(model?.Value))
                    SelectedOllamaModel = OllamaModels.FirstOrDefault(m => m.Name == model.Value);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error initializing settings.");
                IsOllamaURLValid = false;
            }
            finally
            {
                _busy.IsBusy = false;
            }
        }
    }
}