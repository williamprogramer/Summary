using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Summary.Data;
using Summary.Services;

namespace Summary.ViewModels
{
    public partial class SettingsViewModel : ObservableObject
    {
        private readonly BusyService _busy;
        private readonly ILogger<SettingsViewModel> _logger;
        private readonly IDbContextFactory<SummaryDBContext> _contextFactory;

        public SettingsViewModel()
        {
            _busy = App.ServiceProvider.GetRequiredService<BusyService>();
            _logger = App.ServiceProvider.GetRequiredService<ILogger<SettingsViewModel>>();
            _contextFactory = App.ServiceProvider.GetRequiredService<IDbContextFactory<SummaryDBContext>>();
        }
    }
}