using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Summary.Data;
using Summary.Services;
using System;
using System.Threading.Tasks;

namespace Summary.ViewModels
{
    public partial class SettingsViewModel : ObservableObject
    {
        private readonly BusyService _busy;
        private readonly ILogger<SettingsViewModel> _logger;
        private readonly IDbContextFactory<SummaryDBContext> _contextFactory;
        private readonly OllamaService _ollamaService;

        [ObservableProperty]
        public partial bool IsOllamaURLValid { get; set; } = false;
        [ObservableProperty]
        public partial string OllamaUrl { get; set; } = string.Empty;

        public SettingsViewModel()
        {
            _busy = App.ServiceProvider.GetRequiredService<BusyService>();
            _logger = App.ServiceProvider.GetRequiredService<ILogger<SettingsViewModel>>();
            _contextFactory = App.ServiceProvider.GetRequiredService<IDbContextFactory<SummaryDBContext>>();
            _ollamaService = App.ServiceProvider.GetRequiredService<OllamaService>();
        }

        [RelayCommand]
        private async Task ValidateOllamaUrlAsync()
        {
            _busy.IsBusy = true;
            if (!string.IsNullOrWhiteSpace(OllamaUrl))
            {                
                try
                {
                    _ollamaService.OllamaUrl = OllamaUrl;
                    IsOllamaURLValid = await _ollamaService.ValidateOllamaUrl();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error validating Ollama URL.");
                    IsOllamaURLValid = false;
                }
            }
            _busy.IsBusy = false;
        }
    }
}