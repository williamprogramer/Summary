using Microsoft.Extensions.DependencyInjection;
using System.Net.Http;
using System.Threading.Tasks;

namespace Summary.Services
{
    internal class OllamaService
    {
        public string OllamaUrl { get; set; } = string.Empty;
        private readonly HttpClient _httpClient = default!;
        public OllamaService()
        {
            _httpClient = App.ServiceProvider.GetRequiredService<HttpClient>();
        }
        public async Task<bool> ValidateOllamaUrl()
        {
            string response = await _httpClient.GetStringAsync(OllamaUrl);
            return response.Contains("Ollama is running");
        }
    }
}