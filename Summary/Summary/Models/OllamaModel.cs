using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Summary.Models
{
    public class OllamaModel
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;
    }
    public class OllamaResponseModel
    {
        [JsonPropertyName("models")]
        public List<OllamaModel> Models { get; set; } = [];
    }
}