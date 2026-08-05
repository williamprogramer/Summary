using Microsoft.Extensions.DependencyInjection;
using Summary.Models;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace Summary.Services
{
    internal class OllamaService
    {
        private readonly HttpClient _httpClient = default!;
        public OllamaService()
        {
            _httpClient = App.ServiceProvider.GetRequiredService<HttpClient>();
        }

        public async Task<bool> ValidateOllamaUrlAsync(string ollamaUrl)
        {
            string response = await _httpClient.GetStringAsync(NormalizeBaseUrl(ollamaUrl));
            return response.Contains("Ollama is running");
        }

        public async Task<List<OllamaModel>> GetOllamaModelsAsync(string ollamaUrl)
        {
            string response = await _httpClient.GetStringAsync($"{NormalizeBaseUrl(ollamaUrl)}/api/tags");
            OllamaResponseModel? model = JsonSerializer.Deserialize<OllamaResponseModel>(response);
            return model?.Models ?? [];
        }

        private static string NormalizeBaseUrl(string ollamaUrl) => ollamaUrl.TrimEnd('/');
    }
}