using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Summary.Common;
using Summary.Data;
using Summary.Data.Entities;
using System.Threading.Tasks;

namespace Summary.Services
{
    public class SettingsService
    {
        private readonly ILogger<SettingsService> _logger = default!;
        private readonly IDbContextFactory<SummaryDBContext> _dbContextFactory = default!;
        public SettingsService()
        {
            _logger = App.ServiceProvider.GetRequiredService<ILogger<SettingsService>>();
            _dbContextFactory = App.ServiceProvider.GetRequiredService<IDbContextFactory<SummaryDBContext>>();
        }

        public async Task<SettingsEntity?> GetSettingByKeyAsync(string key)
        {
            using SummaryDBContext context = await _dbContextFactory.CreateDbContextAsync();
            return await context.Settings.FirstOrDefaultAsync(s => s.Key == key);
        }

        public async Task CreateKeysIfMissingAsync()
        {
            using SummaryDBContext context = await _dbContextFactory.CreateDbContextAsync();
            SettingsEntity? uri = await context.Settings.FirstOrDefaultAsync(s => s.Key == SummaryConstants.OLLAMA_URI);
            SettingsEntity? model = await context.Settings.FirstOrDefaultAsync(s => s.Key == SummaryConstants.OLLAMA_MODEL);
            if (uri is null)
            {
                uri = new SettingsEntity { Key = SummaryConstants.OLLAMA_URI, Value = string.Empty };
                context.Settings.Add(uri);
            }
            if (model is null)
            {
                model = new SettingsEntity { Key = SummaryConstants.OLLAMA_MODEL, Value = string.Empty };
                context.Settings.Add(model);
            }
            await context.SaveChangesAsync();
        }
    }
}