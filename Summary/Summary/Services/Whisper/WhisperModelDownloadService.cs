using Microsoft.Extensions.Logging;
using Summary.Helpers;
using System.Threading;
using System.Threading.Tasks;

namespace Summary.Services.Whisper
{
    public sealed class WhisperModelDownloadService
    {
        public const string HttpClientName = HuggingFaceModelDownloadService.HttpClientName;
        private const string BaseUrl = "https://huggingface.co/Xenova/whisper-large/resolve/main/";

        private static readonly (string UrlPath, string FileName)[] Files =
        [
            ("vocab.json", "vocab.json"),
            ("tokenizer.json", "tokenizer.json"),
            ("tokenizer_config.json", "tokenizer_config.json"),
            ("special_tokens_map.json", "special_tokens_map.json"),
            ("added_tokens.json", "added_tokens.json"),
            ("merges.txt", "merges.txt"),
            ("config.json", "config.json"),
            ("generation_config.json", "generation_config.json"),
            ("preprocessor_config.json", "preprocessor_config.json"),
            ("onnx/encoder_model_quantized.onnx", "encoder_model_quantized.onnx"),
            ("onnx/decoder_model_quantized.onnx", "decoder_model_quantized.onnx"),
            ("onnx/decoder_with_past_model_quantized.onnx", "decoder_with_past_model_quantized.onnx")
        ];

        private readonly HuggingFaceModelDownloadService _downloader;
        private readonly ILogger<WhisperModelDownloadService> _logger;

        public WhisperModelDownloadService(
            HuggingFaceModelDownloadService downloader,
            ILogger<WhisperModelDownloadService> logger)
        {
            _downloader = downloader;
            _logger = logger;
        }

        public Task EnsureModelsAsync(CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("Ensuring Whisper large model files.");
            return _downloader.EnsureFilesAsync(
                BaseUrl,
                PathHelper.WhisperLargePath,
                Files,
                "Whisper large",
                cancellationToken);
        }
    }
}
